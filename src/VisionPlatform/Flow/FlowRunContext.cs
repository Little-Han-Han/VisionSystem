using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using VisionPlatform.Camera;
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
        Skipped
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
            Shapes = new List<IGeometryShape>();
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
        /// <summary>被执行的流程。</summary>
        public FlowDefinition Flow { get; set; }

        /// <summary>取图用的相机；本地图片模式下可为 null。</summary>
        public ICamera Camera { get; set; }

        /// <summary>单链节点顺序。</summary>
        public List<FlowNode> Chain { get; set; }

        /// <summary>下一个待执行节点的下标。</summary>
        public int Cursor { get; set; }

        /// <summary>当前工作帧（可能被预处理节点替换）。</summary>
        public Bitmap Current { get; set; }

        /// <summary>图像源节点产出的原始帧副本（供「原始图像」预览模式）。</summary>
        public Bitmap SourceFrame { get; set; }

        /// <summary>工作帧相对原始帧的 X 偏移（图像源 ROI 裁剪累积）。</summary>
        public double OffsetX { get; set; }

        /// <summary>工作帧相对原始帧的 Y 偏移（图像源 ROI 裁剪累积）。</summary>
        public double OffsetY { get; set; }

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

        /// <summary>记录实时位姿（供下游位置修正节点取基准）。</summary>
        public void SetPose(double cx, double cy, double angle, double scale)
        {
            PoseCx = cx;
            PoseCy = cy;
            PoseAngle = angle;
            PoseScale = scale;
            HasPose = true;
        }

        /// <summary>替换当前工作帧并释放旧帧。</summary>
        public void ReplaceFrame(Bitmap next)
        {
            if (!object.ReferenceEquals(Current, next) && Current != null)
                Current.Dispose();
            Current = next;
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
