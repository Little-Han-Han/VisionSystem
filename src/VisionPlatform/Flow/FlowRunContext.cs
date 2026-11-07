using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using VisionPlatform.Camera;
using VisionPlatform.Native;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.Flow
{
    /// <summary>单个节点在一次运行中的执行状态。</summary>
    public enum FlowNodeStatus
    {
        /// <summary>尚未执行。</summary>
        Pending,

        /// <summary>正在执行（仅单步模式下会被外部观察到）。</summary>
        Running,

        /// <summary>执行成功且判定通过。</summary>
        Ok,

        /// <summary>执行失败或判定不通过。</summary>
        Ng,

        /// <summary>被跳过（无输入帧、未知节点类型等）。</summary>
        Skipped,

        /// <summary>
        /// 执行成功但有需要人注意的情况（如 OCR 模板库为空，只做分割不输出文本）。
        /// <para>
        /// <strong>语义边界</strong>：<c>Warn</c> 只影响显示，<strong>永远不等于 NG</strong> ——
        /// 流程判定、下游联动、结果汇总都不得把它当失败处理。
        /// 之所以不沿用 <c>Ok</c>：<c>OK</c> 在本系统里是"检测通过"的语义，
        /// 把"根本没识别"显示成 OK 会让操作员把未识别的工件判为良品（这是误判风险，不是美化问题）。
        /// </para>
        /// </summary>
        Warn
    }

    /// <summary>
    /// 执行期位置修正变换（<c>RunTransform</c>）。
    /// </summary>
    /// <remarks>
    /// <para>语义（PRD 假设 B）：实时位姿相对基准位姿的偏差
    /// <c>dX = Cx - Rx</c>、<c>dY = Cy - Ry</c>、<c>dAngle = Angle - Ra</c>、<c>dScale = Scale</c>。</para>
    /// <para>下游带 ROI 节点在使用 ROI 前统一走 <see cref="ApplyToRect"/>：
    /// <strong>先绕基准点 (Rx, Ry) 旋转 dAngle，再平移 (dX, dY)</strong>；
    /// <c>EnableAngle=0</c> 时由位置修正节点把 dAngle 置 0，本类只按 dAngle 计算。</para>
    /// <para><strong>仅存在于执行期对象，禁止写回 <see cref="FlowNode.Params"/></strong>：
    /// 每帧都在变，写回会触发配置落盘并跨帧污染。</para>
    /// <para>链内多个位置修正节点<strong>以最后一个为准（覆盖式），不累加</strong>。</para>
    /// </remarks>
    public sealed class FlowRunTransform
    {
        /// <summary>基准 X（工作帧坐标）。</summary>
        public double RefX { get; private set; }

        /// <summary>基准 Y（工作帧坐标）。</summary>
        public double RefY { get; private set; }

        /// <summary>基准角度（度）。</summary>
        public double RefAngle { get; private set; }

        /// <summary>实时 X 相对基准的偏移。</summary>
        public double DX { get; private set; }

        /// <summary>实时 Y 相对基准的偏移。</summary>
        public double DY { get; private set; }

        /// <summary>实时角度相对基准的偏差（度）；EnableAngle=0 时为 0。</summary>
        public double DAngle { get; private set; }

        /// <summary>实时缩放（1.0 = 无缩放）。</summary>
        public double DScale { get; private set; }

        /// <summary>是否已经写入过有效的位置修正（未写入时下游 ROI 保持不变）。</summary>
        public bool IsValid { get; private set; }

        /// <summary>
        /// 本帧的修正是否**已经烘进像素**（位置修正节点把图 warp 回基准姿态后 <see cref="Reset"/>，
        /// 下游 ROI 用原坐标即可）。
        /// <para>
        /// 【为什么需要单独一个标记】Reset 之后 <see cref="IsValid"/> 为 false，
        /// 下游无法区分"修正过（图已对齐）"与"压根没修正" —— 现场就变成
        /// "看起来 ROI 没动，到底生效没有？"。这个标记让摘要能给出明确答案。
        /// </para>
        /// </summary>
        public bool Warped { get; private set; }

        /// <summary>清空为「无修正」状态。</summary>
        public void Reset()
        {
            RefX = 0.0;
            RefY = 0.0;
            RefAngle = 0.0;
            DX = 0.0;
            DY = 0.0;
            DAngle = 0.0;
            DScale = 1.0;
            IsValid = false;
            Warped = false;
        }

        /// <summary>标记"本帧已把修正烘进像素"（由位置修正节点在 warp 后调用）。</summary>
        public void MarkWarped()
        {
            Warped = true;
            IsValid = false;      // 图已对齐，下游不再做二次坐标映射
        }

        /// <summary>
        /// 给下游节点的摘要用的一句话：ROI 到底有没有被修正、以哪种方式。
        /// 空串 = 本帧没有位置修正（用户就该去查为什么没生效）。
        /// </summary>
        public string PoseNote()
        {
            if (Warped) return "　· 图像已按位置修正对齐";
            if (IsValid)
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "　· ROI 已按位置修正映射(dX={0:F1},dY={1:F1},dA={2:F2}°)", DX, DY, DAngle);
            }
            return "";
        }

        /// <summary>写入一次修正结果（覆盖式）。</summary>
        public void Set(double refX, double refY, double refAngle,
                        double dx, double dy, double dAngle, double dScale)
        {
            RefX = refX;
            RefY = refY;
            RefAngle = refAngle;
            DX = dx;
            DY = dy;
            DAngle = dAngle;
            DScale = dScale;
            IsValid = true;
        }

        /// <summary>把角度叠加 dAngle（用于下游方向型量，如卡尺扫描角）。</summary>
        public double ApplyToAngle(double angleDeg)
        {
            return IsValid ? (angleDeg + DAngle) : angleDeg;
        }

        /// <summary>把点变换到实际位置（先绕基准点旋转，再平移）。</summary>
        public PointF ApplyToPoint(double x, double y)
        {
            if (!IsValid) return new PointF((float)x, (float)y);

            double rx = x - RefX;
            double ry = y - RefY;
            double a = DAngle * Math.PI / 180.0;
            double cos = Math.Cos(a);
            double sin = Math.Sin(a);

            // 图像坐标 y 轴向下，[cos,-sin; sin,cos] 即顺时针为正（与全库角度约定一致）
            double px = RefX + cos * rx - sin * ry + DX;
            double py = RefY + sin * rx + cos * ry + DY;
            return new PointF((float)px, (float)py);
        }

        /// <summary>
        /// 把 ROI 变换到实际位置：四角绕基准点旋转 dAngle 后平移 (dX, dY)，
        /// 返回旋转后的<strong>轴对齐外接矩形</strong>（宽高为 0 时只变换位置）。
        /// </summary>
        public Rectangle ApplyToRect(Rectangle roi)
        {
            if (!IsValid) return roi;

            if (roi.Width <= 0 || roi.Height <= 0)
            {
                PointF p = ApplyToPoint(roi.X, roi.Y);
                return new Rectangle((int)Math.Round(p.X), (int)Math.Round(p.Y), roi.Width, roi.Height);
            }

            PointF c0 = ApplyToPoint(roi.Left, roi.Top);
            PointF c1 = ApplyToPoint(roi.Right, roi.Top);
            PointF c2 = ApplyToPoint(roi.Right, roi.Bottom);
            PointF c3 = ApplyToPoint(roi.Left, roi.Bottom);

            double minX = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X));
            double minY = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y));
            double maxX = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X));
            double maxY = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y));

            int x = (int)Math.Round(minX);
            int y = (int)Math.Round(minY);
            int w = Math.Max(1, (int)Math.Round(maxX) - x);
            int h = Math.Max(1, (int)Math.Round(maxY) - y);
            return new Rectangle(x, y, w, h);
        }
    }

    /// <summary>单个节点执行后的进度（供画布状态标注与预览区回看）。</summary>
    public sealed class FlowNodeProgress
    {
        /// <summary>
        /// 节点自己上报的<strong>类型化输出</strong>（长度 / 角度 / 有效点数 / 剔除数 …）。
        /// <see cref="FlowRunSession.Outputs"/> 收集时合并进 <see cref="NodeOutput.Values"/>。
        /// </summary>
        public List<PortValue> Outputs = new List<PortValue>();

        /// <summary>节点上报的逐个目标几何（Blob 每个区域 / 匹配每个目标）。</summary>
        public List<RegionInfo> Regions = new List<RegionInfo>();

        /// <summary>
        /// 节点生成的<strong>位图叠加层</strong>（与输入帧同尺寸、带透明通道）。
        /// 分割掩膜上色、深度伪彩图这类"像素级效果"矢量图形画不出来，走这个通道；
        /// 预览面板会把它与帧合成后显示，矢量层仍画在最上面。
        /// </summary>
        public Bitmap Overlay;

        /// <summary>节点 Id。</summary>
        public string NodeId { get; set; }

        /// <summary>节点显示名。</summary>
        public string NodeName { get; set; }

        /// <summary>在链中的序号（从 1 开始）。</summary>
        public int Index { get; set; }

        /// <summary>链中节点总数。</summary>
        public int Total { get; set; }

        /// <summary>执行状态。</summary>
        public FlowNodeStatus Status { get; set; }

        /// <summary>该节点自身耗时（毫秒，整数）。</summary>
        public int ElapsedMs { get; set; }

        /// <summary>该节点的结论摘要（一行）。</summary>
        public string Summary { get; set; }

        /// <summary>该节点"主判据"的结构化取值（结果区/画布/预览五处共用，见 <see cref="FlowNodeMetrics"/>）。</summary>
        public FlowNodeMetrics Metrics { get; set; }

        /// <summary>
        /// 该节点产出的<strong>辅助</strong>叠加图形（ROI 轮廓、卡尺窗口等"输入几何"）。
        /// <para>与 <see cref="Shapes"/> 的分工：<c>Shapes</c> = 检测目标（会进结果区明细、可点行高亮），
        /// <c>AuxShapes</c> = 辅助几何（只画在画布上，不占结果区行）。混在一起会让
        /// "12 个 Blob"下面多出 4 条 ROI 边线行，条数永远对不上。</para>
        /// </summary>
        public List<IGeometryShape> AuxShapes { get; set; }

        /// <summary>该节点<strong>自身</strong>产出的叠加图形（图像坐标）。</summary>
        public List<IGeometryShape> Shapes { get; set; }

        /// <summary>
        /// 该节点执行后的输出帧（预览区回看用）。仅在会话开启
        /// <see cref="FlowRunSession.CaptureFrames"/> 时才有值，否则为 null。
        /// 所有权归会话，随会话释放。
        /// </summary>
        public Bitmap Frame { get; set; }

        public FlowNodeProgress()
        {
            NodeId = "";
            NodeName = "";
            Summary = "";
            Status = FlowNodeStatus.Pending;
            Metrics = new FlowNodeMetrics();
            Shapes = new List<IGeometryShape>();
            AuxShapes = new List<IGeometryShape>();
        }

        public override string ToString()
        {
            return string.Format("{0}/{1} {2} {3} {4}ms",
                Index, Total, NodeName, Status, ElapsedMs);
        }
    }

    /// <summary>
    /// 一次流程运行的会话状态：当前帧、节点游标、逐节点进度、位置修正变换。
    /// </summary>
    /// <remarks>
    /// 【坐标偏移】<see cref="OffsetX"/> / <see cref="OffsetY"/> 记录当前工作帧相对
    /// <strong>原始抓帧</strong>的左上角偏移（由图像源 ROI 裁剪累积而来）。
    /// 节点参数里的 ROI 由用户按<strong>原始图坐标</strong>填写，使用前必须减去该偏移
    /// （见 FlowRunner 的 <c>ToWorking*</c> 系列换算）；而产出的叠加图形一律使用
    /// <strong>工作帧坐标</strong>，与预览显示的图像对齐。
    /// </remarks>
    public sealed class FlowRunSession : IDisposable
    {

        /// <summary>
        /// 本会话里"刚示教过位置修正基准"的节点 Id。
        /// <para>
        /// 【为什么要放在会话里】示教是节点级事件，但宿主只在"链尾快照"里看到它 ——
        /// 单步执行（StepOnce）根本没有链尾快照，于是基准永远没落盘，下次运行又示教成 0 偏差，
        /// 现场表现就是"位置修正时好时坏、坐标有时不变"。
        /// </para>
        /// </summary>
        public readonly HashSet<string> BaselineTaughtNodes = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 本轮"每个节点产出了什么"的统一台账（按执行顺序）。脚本节点 / 结果输出 / 数据联动都从这里取。
        /// </summary>
        public readonly List<NodeOutput> Outputs = new List<NodeOutput>();

        /// <summary>
        /// 脚本节点写的**命名输出**（名 → 值）。下游脚本节点按名字读取，实现跨节点传值。
        /// </summary>
        public readonly Dictionary<string, object> NamedValues =
            new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>被执行的流程。</summary>
        public FlowDefinition Flow { get; set; }

        /// <summary>取图用的相机；本地图片模式下可为 null。</summary>
        public ICamera Camera { get; set; }

        /// <summary>拓扑序节点列表（DAG：扇出 / 汇合均按此顺序串行执行）。</summary>
        public List<FlowNode> Chain { get; set; }

        /// <summary>下一个待执行节点的下标。</summary>
        public int Cursor { get; set; }

        /// <summary>当前工作帧（可能被预处理节点替换）。</summary>
        public Bitmap Current { get; set; }

        /// <summary>
        /// 是否保存"刚采进来的原图"副本（<see cref="SourceFrame"/>）。
        /// <para>只有需要显示原图的流程预览才置 true；并行生产运行置 false，
        /// 省掉每帧一次整幅 Bitmap 克隆。</para>
        /// </summary>
        public bool CaptureSourceFrame { get; set; }

        /// <summary>
        /// 是否为节点保留"它当时处理的画面"（<see cref="FlowNodeProgress.Frame"/>）。
        /// <para>单步 / 预览需要；并行槽（MainForm 全部启动）不看节点帧，置 false
        /// 可省掉每帧 N 次克隆与随之而来的大对象内存压力。</para>
        /// </summary>
        public bool CaptureProgressFrames { get; set; } = true;

        /// <summary>图像源节点产出的原始帧副本（供「原始图像」预览模式）。</summary>
        public Bitmap SourceFrame { get; set; }

        /// <summary>工作帧相对原始帧的 X 偏移（图像源 ROI 裁剪累积）。</summary>
        public double OffsetX { get; set; }

        /// <summary>工作帧相对原始帧的 Y 偏移（图像源 ROI 裁剪累积）。</summary>
        public double OffsetY { get; set; }

        /// <summary>
        /// <strong>彩色通道</strong>的当前帧（24bpp / 32bpp BGR）；无彩色帧时为 null。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 为什么需要单独一条通道：<see cref="VisionAlgo.GrayInput"/>（原生封装内）无条件把
        /// 输入位图转成 8bpp 灰度，算法库拿到的永远是 channels=1，颜色信息在进算法前就丢了。
        /// 颜色提取是首个必须拿彩色像素的算法，因此这里单独保留一个彩色引用。
        /// </para>
        /// <para>
        /// 【所有权】<strong>只持引用，不 Clone、不 Dispose</strong>：它要么就是
        /// <see cref="Current"/> 本身，要么指向刚被 <see cref="ReplaceFrame"/> 换成 Current 的那张图，
        /// 生命周期完全跟随 <see cref="Current"/>，由 <see cref="ReplaceFrame"/> / <see cref="Dispose"/>
        /// 统一释放，本通道不重复释放（否则 double-dispose）。
        /// </para>
        /// <para>
        /// 由 <see cref="SyncColorInput"/> 在<strong>每个节点执行完后</strong>按当前工作帧的
        /// 像素格式刷新，不依赖各节点自觉汇报 —— 避免漏改某个节点后残留指向已释放位图的野引用。
        /// </para>
        /// </remarks>
        public Bitmap ColorInput { get; set; }

        /// <summary>逐节点进度。</summary>
        public List<FlowNodeProgress> Progress { get; private set; }

        /// <summary>位置修正变换（覆盖式）。</summary>
        public FlowRunTransform Transform { get; private set; }

        /// <summary>累积结论（全部节点都通过才为 true）。</summary>
        public bool Passed { get; set; }

        /// <summary>各节点摘要，按顺序收集。</summary>
        public List<string> Summaries { get; private set; }

        /// <summary>整体计时器。</summary>
        public Stopwatch Watch { get; set; }

        /// <summary>是否因致命错误中止（如取帧失败）。</summary>
        public bool Aborted { get; set; }

        /// <summary>致命错误原因（<see cref="Aborted"/> 时有效）。</summary>
        public string ErrorMessage { get; set; }

        /// <summary>是否为每个节点保存输出帧副本（单步预览需要，连续运行应关闭以省内存）。</summary>
        public bool CaptureFrames { get; set; }

        /// <summary>上游<strong>最近一个</strong>匹配类节点第 1 个目标的实时位姿。</summary>
        public bool HasPose { get; set; }

        /// <summary>实时位姿中心 X（工作帧坐标）。</summary>
        public double PoseCx { get; set; }

        /// <summary>实时位姿中心 Y（工作帧坐标）。</summary>
        public double PoseCy { get; set; }

        /// <summary>实时位姿角度（度）。</summary>
        public double PoseAngle { get; set; }

        /// <summary>实时位姿缩放。</summary>
        public double PoseScale { get; set; }

        /// <summary>是否还有未执行的节点（或已中止）。</summary>
        public bool IsDone
        {
            get
            {
                if (Aborted) return true;
                if (Chain == null) return true;
                return Cursor >= Chain.Count;
            }
        }

        public FlowRunSession()
        {
            Chain = new List<FlowNode>();
            Progress = new List<FlowNodeProgress>();
            Transform = new FlowRunTransform();
            Summaries = new List<string>();
            Passed = true;
            ErrorMessage = "";
            PoseScale = 1.0;
            Watch = new Stopwatch();
        }

        /// <summary>各匹配类节点最近一次输出的位姿（nodeId → [cx, cy, angle, scale]）。
        /// 供位置修正按端子连线获取指定上游节点的位姿（端子级语义，替代全局最近位姿）。</summary>
        public readonly Dictionary<string, double[]> NodePoses = new Dictionary<string, double[]>(StringComparer.Ordinal);

        /// <summary>记录实时位姿（供下游位置修正节点取基准）。</summary>
        public void SetPose(double cx, double cy, double angle, double scale)
        {
            PoseCx = cx;
            PoseCy = cy;
            PoseAngle = angle;
            PoseScale = scale;
            HasPose = true;
        }

        /// <summary>记录指定节点输出的位姿（nodeId → [cx, cy, angle, scale]）。</summary>
        public void SetNodePose(string nodeId, double cx, double cy, double angle, double scale)
        {
            NodePoses[nodeId] = new[] { cx, cy, angle, scale };
        }

        /// <summary>读取指定节点最近一次输出的位姿；无记录返回 false。</summary>
        public bool TryGetNodePose(string nodeId, out double[] pose)
        {
            return NodePoses.TryGetValue(nodeId, out pose);
        }

        /// <summary>替换当前工作帧并释放旧帧。</summary>
        public void ReplaceFrame(Bitmap next)
        {
            if (!object.ReferenceEquals(Current, next) && Current != null)
                Current.Dispose();
            Current = next;
        }

        /// <summary>
        /// 按当前工作帧的像素格式刷新 <see cref="ColorInput"/>（每个节点执行完后调用一次）。
        /// </summary>
        /// <remarks>
        /// 判定规则：<strong>工作帧本身是 24bpp / 32bpp 彩色图 → 它就是彩色通道的当前值；
        /// 灰度 / 二值图（像素已被改动，颜色不再对应）→ 清空</strong>。
        /// 这样「图像源出彩色帧」「滤波保持彩色」「二值化 / 预处理后无彩色」都自然成立，
        /// 且不会出现「指向已被 ReplaceFrame 释放的位图」的野引用。
        /// </remarks>
        public void SyncColorInput()
        {
            ColorInput = (Current != null && VisionAlgo.IsColorFormat(Current.PixelFormat))
                ? Current
                : null;
        }

        /// <summary>释放工作帧、原始帧副本与各节点帧副本。</summary>
        public void Dispose()
        {
            if (Current != null) { Current.Dispose(); Current = null; }
            if (SourceFrame != null) { SourceFrame.Dispose(); SourceFrame = null; }

            foreach (FlowNodeProgress p in Progress)
            {
                if (p != null && p.Frame != null) { p.Frame.Dispose(); p.Frame = null; }
            }
        }
    }
}
