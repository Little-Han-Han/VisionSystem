using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>
    /// QuickBuild 风格的<strong>作业树</strong>：工具一行、端子缩进列在下面，连线在行间走直角。
    /// <para>
    /// 与画布（<see cref="FlowCanvasControl"/>）是同一份数据的两种视图：
    /// 树视图用于"读顺序、看每步输出"，画布用于"自由摆放、拖线"。
    /// </para>
    /// </summary>
    public sealed class FlowTreeView : ScrollableControl
    {
        private const int RootH = 28;        // 「作业」根行高度
        private const int HeaderH = 26;      // 工具行高
        private const int PortH = 20;        // 端子行高
        private const int Indent = 18;       // 端子行缩进
        private const int DotSize = 7;

        private sealed class Row
        {
            public FlowNode Node;
            public FlowPortDef Port;         // null = 工具标题行
            public FlowPortDir Dir;
            public int Index;
            public int Top;
            public int Height;
        }

        private readonly List<Row> _rows = new List<Row>();
        private FlowDefinition _flow;
        private string _selectedNodeId = "";

        /// <summary>运行状态来源（由宿主提供；为空时全部按"未运行"画）。</summary>
        public Func<string, FlowNodeRunInfo> StatusProvider { get; set; }

        /// <summary>节点被点选（宿主据此刷新右侧参数面板）。</summary>
        public event EventHandler<FlowNodeEventArgs> NodeSelected;

        public FlowTreeView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Color.FromArgb(24, 24, 32);
        }

        /// <summary>当前显示的流程。</summary>
        public FlowDefinition Flow
        {
            get { return _flow; }
            set { _flow = value; Rebuild(); }
        }

        /// <summary>当前选中的节点 Id（空 = 无）。</summary>
        public string SelectedNodeId
        {
            get { return _selectedNodeId; }
            set
            {
                _selectedNodeId = value ?? "";
                Invalidate();
            }
        }

        /// <summary>重建行模型（流程结构变化时调用）。</summary>
        public void Rebuild()
        {
            _rows.Clear();

            List<FlowNode> order = null;
            if (_flow != null)
            {
                string err;
                FlowOrder.Build(_flow, out order, out err);
            }

            int y = 8 + RootH;
            if (order != null)
            {
                foreach (FlowNode n in order)
                {
                    if (n == null) continue;

                    _rows.Add(new Row { Node = n, Port = null, Top = y, Height = HeaderH });
                    y += HeaderH;

                    FlowNodeType t;
                    if (!n.TryGetType(out t)) continue;

                    List<FlowPortDef> ins = FlowPortSchema.InputsOf(t);
                    for (int i = 0; i < ins.Count; i++)
                    {
                        _rows.Add(new Row { Node = n, Port = ins[i], Dir = FlowPortDir.In, Index = i, Top = y, Height = PortH });
                        y += PortH;
                    }

                    List<FlowPortDef> outs = FlowPortSchema.OutputsOf(t);
                    for (int i = 0; i < outs.Count; i++)
                    {
                        _rows.Add(new Row { Node = n, Port = outs[i], Dir = FlowPortDir.Out, Index = i, Top = y, Height = PortH });
                        y += PortH;
                    }

                    y += 6;   // 工具之间的间隙
                }
            }

            AutoScrollMinSize = new Size(0, y + 8);
            Invalidate();
        }

        // =====================================================================
        //  绘制
        // =====================================================================

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(0, -AutoScrollPosition.Y);

            using (Brush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, 0, 0, Width, Math.Max(Height, AutoScrollMinSize.Height));

            if (_flow == null || _rows.Count == 0)
            {
                using (Brush b = new SolidBrush(UiTheme.ColorTextSecondary))
                    g.DrawString("还没有工具：从左边工具箱拖一个进来，或点「新建流程」", UiTheme.FontBody, b, 16, 16);
                return;
            }

            DrawRootRow(g);
            DrawLinks(g);

            foreach (Row r in _rows)
                if (r.Port == null) DrawToolRow(g, r); else DrawPortRow(g, r);
        }

        /// <summary>QuickBuild 顶部的「作业」根行。</summary>
        private void DrawRootRow(Graphics g)
        {
            var rect = new RectangleF(4, 8, Width - 8, RootH);
            using (Brush bg = new SolidBrush(Color.FromArgb(44, 46, 58)))
                g.FillRectangle(bg, rect);

            string title = "▼  作业：" + (_flow != null && !string.IsNullOrEmpty(_flow.Name) ? _flow.Name : "未命名");
            using (Brush b = new SolidBrush(UiTheme.ColorTextPrimary))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(title, UiTheme.FontGroupTitle, b, new RectangleF(rect.X + 10, rect.Y, rect.Width - 20, rect.Height), sf);
        }

        private void DrawToolRow(Graphics g, Row r)
        {
            FlowNode n = r.Node;
            bool selected = string.Equals(n.Id, _selectedNodeId, StringComparison.Ordinal);

            var rect = new RectangleF(4, r.Top, Width - 8, r.Height);

            using (Brush bg = new SolidBrush(selected ? Color.FromArgb(52, 58, 76) : Color.FromArgb(34, 34, 42)))
                g.FillRectangle(bg, rect);

            // 类型色条
            using (Brush stripe = new SolidBrush(TypeColor(n)))
                g.FillRectangle(stripe, rect.X, rect.Y, 4f, rect.Height);

            // 名称
            FlowNodeRunInfo run = LookupRun(n.Id);
            string title = string.IsNullOrEmpty(n.Name) ? n.Type : n.Name;
            var textRect = new RectangleF(rect.X + 14, rect.Y, rect.Width - 120, rect.Height);
            using (Brush b = new SolidBrush(UiTheme.ColorTextPrimary))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(title, UiTheme.FontGroupTitle, b, textRect, sf);

            // 右侧状态
            string state = "";
            Color stateColor = UiTheme.ColorTextSecondary;
            if (run != null)
            {
                if (run.State == FlowNodeRunState.Ok) { state = "OK"; stateColor = UiTheme.ColorOk; }
                else if (run.State == FlowNodeRunState.Failed) { state = "NG"; stateColor = UiTheme.ColorNg; }
                else if (run.State == FlowNodeRunState.Warn) { state = "警告"; stateColor = UiTheme.ColorWarn; }
                else if (run.State == FlowNodeRunState.Running) { state = "运行中"; stateColor = UiTheme.ColorRunning; }
                else { state = "未运行"; }

                if (run.ElapsedMs >= 0) state += "  " + run.ElapsedMs + " ms";
            }

            var stateRect = new RectangleF(rect.Right - 120, rect.Y, 112, rect.Height);
            using (Brush b = new SolidBrush(stateColor))
            using (var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                g.DrawString(state, UiTheme.FontSmall, b, stateRect, sf);
        }

        private void DrawPortRow(Graphics g, Row r)
        {
            if (r.Port == null) return;

            bool isIn = r.Dir == FlowPortDir.In;
            float x = 10 + Indent;
            float cy = r.Top + r.Height * 0.5f;

            // 端子小方块（左右位置区分输入/输出：这里统一放左，靠箭头符号区分）
            var dot = new RectangleF(x, cy - DotSize * 0.5f, DotSize, DotSize);
            using (Brush b = new SolidBrush(KindColor(r.Port.Kind))) g.FillRectangle(b, dot);

            string label = (isIn ? "◀ " : "▶ ") + r.Port.DisplayName;
            var textRect = new RectangleF(x + DotSize + 6, r.Top, Width - (x + DotSize + 6) - 12, r.Height);
            using (Brush b = new SolidBrush(isIn ? UiTheme.ColorTextSecondary : UiTheme.ColorTextPrimary))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(label, UiTheme.FontSmall, b, textRect, sf);
        }

        /// <summary>
        /// 连线：源"输出端子行" → 目标"输入端子行"，直角折线走行间隙，不压字。
        /// 通道列按目标顺序错开，避免多条线重叠在一起。
        /// </summary>
        private void DrawLinks(Graphics g)
        {
            if (_flow == null || _flow.Links == null) return;

            int lane = 0;
            foreach (FlowLink l in _flow.Links)
            {
                if (l == null) continue;

                Row src = FindRow(l.FromNodeId, FlowPortDir.Out, l.FromPort ?? "");
                Row dst = FindRow(l.ToNodeId, FlowPortDir.In, l.ToPort ?? "");
                if (src == null || dst == null) continue;

                float y1 = src.Top + src.Height - 2f;
                float y2 = dst.Top + dst.Height - 2f;
                float x1 = Width - 14f;
                float x2 = 10f + Indent + DotSize * 0.5f;

                // 通道放在右侧留白里，最多往里收 5 条，避免绕一大圈
                float laneX = Math.Max(x2 + 40f, x1 - 10f - lane * 8f);
                lane = (lane + 1) % 6;

                Color color = KindColor(src.Port != null ? src.Port.Kind : FlowPortKind.Any);
                using (var pen = new Pen(Color.FromArgb(150, color), 1.4f))
                {
                    g.DrawLine(pen, x1, y1, laneX, y1);
                    g.DrawLine(pen, laneX, y1, laneX, y2);
                    g.DrawLine(pen, laneX, y2, x2 + 4f, y2);
                }
            }
        }

        // =====================================================================
        //  命中
        // =====================================================================

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            Row hit = RowAt(e.Y + AutoScrollPosition.Y);
            if (hit == null) return;

            _selectedNodeId = hit.Node.Id;
            Invalidate();

            EventHandler<FlowNodeEventArgs> h = NodeSelected;
            if (h != null) h(this, new FlowNodeEventArgs(hit.Node));
        }

        private Row RowAt(int y)
        {
            foreach (Row r in _rows)
                if (y >= r.Top && y < r.Top + r.Height) return r;
            return null;
        }

        private Row FindRow(string nodeId, FlowPortDir dir, string portKey)
        {
            if (string.IsNullOrEmpty(nodeId)) return null;

            Row fallback = null;
            foreach (Row r in _rows)
            {
                if (r.Port == null || r.Dir != dir) continue;
                if (!string.Equals(r.Node.Id, nodeId, StringComparison.Ordinal)) continue;

                if (string.IsNullOrEmpty(portKey)) { if (fallback == null) fallback = r; continue; }
                if (r.Port != null && string.Equals(r.Port.Key, portKey, StringComparison.Ordinal)) return r;
            }
            return fallback;
        }

        private FlowNodeRunInfo LookupRun(string nodeId)
        {
            Func<string, FlowNodeRunInfo> p = StatusProvider;
            return p == null ? null : p(nodeId);
        }

        // =====================================================================
        //  配色（与画布一致）
        // =====================================================================

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
                case FlowPortKind.Text: return Color.FromArgb(190, 195, 205);
                default: return Color.FromArgb(200, 220, 120);
            }
        }

        private static Color TypeColor(FlowNode n)
        {
            FlowNodeType t;
            if (n == null || !n.TryGetType(out t)) return UiTheme.ColorTextSecondary;

            switch (t)
            {
                case FlowNodeType.ImageSource: return Color.FromArgb(110, 210, 235);
                case FlowNodeType.Preprocess:
                case FlowNodeType.ImageFilter: return Color.FromArgb(140, 200, 170);
                case FlowNodeType.ShapeMatch:
                case FlowNodeType.TemplateMatch:
                case FlowNodeType.PositionCorrect: return Color.FromArgb(190, 150, 245);
                case FlowNodeType.Blob:
                case FlowNodeType.ColorExtractor:
                case FlowNodeType.ObjectDetect:
                case FlowNodeType.Ocr: return Color.FromArgb(255, 185, 95);
                case FlowNodeType.LineCaliper:
                case FlowNodeType.CircleCaliper:
                case FlowNodeType.FitLine:
                case FlowNodeType.FitCircle:
                case FlowNodeType.Sharpness: return Color.FromArgb(140, 220, 150);
                case FlowNodeType.Script: return Color.FromArgb(220, 180, 255);
                default: return Color.FromArgb(160, 170, 190);
            }
        }
    }
}
