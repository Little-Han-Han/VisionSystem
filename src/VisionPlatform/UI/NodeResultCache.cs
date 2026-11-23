using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using VisionPlatform.Flow;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 一个节点"最近一次运行结果"的缓存（<strong>只缓存数值与图形，不缓存整帧</strong>）。
    /// <para>
    /// 【为什么不留帧】每节点一帧 5MP ≈ 20MB，5 节点 = 100MB，8 小时运行的内存红线必挂。
    /// 但"切回历史节点点行高亮"又需要能看到图，所以只留一张 <see cref="Thumbnail"/>（1/4 缩略帧，
    /// ≈1.25MB/节点），<strong>仅服务历史回看高亮，绝不参与任何检测计算</strong>。
    /// </para>
    /// <para>生命周期：由 <see cref="FlowResultPanel"/> 持有；换节点覆盖、换流程清空。缩略帧随缓存一起 Dispose。</para>
    /// </summary>
    public sealed class NodeResultCache
    {
        /// <summary>结果图形列表的截断上限（防止 Blob 上千个目标把结果区拖死）。</summary>
        public const int MaxShapes = 500;

        /// <summary>所属流程 Id。</summary>
        public string FlowId = "";

        /// <summary>节点 Id。</summary>
        public string NodeId = "";

        /// <summary>节点名。</summary>
        public string NodeName = "";

        /// <summary>链内序号（从 1 开始）。</summary>
        public int Index;

        /// <summary>链内节点总数。</summary>
        public int Total;

        /// <summary>节点类型（决定主判据文案用哪些字段）。</summary>
        public FlowNodeType Type = FlowNodeType.ResultOutput;

        /// <summary>执行状态。</summary>
        public FlowNodeStatus Status = FlowNodeStatus.Pending;

        /// <summary>节点耗时（毫秒）。</summary>
        public int ElapsedMs;

        /// <summary>该次运行的完成时间（结果区显示 HH:mm:ss）。</summary>
        public DateTime RunAt = DateTime.MinValue;

        /// <summary>结构化判据（主行文案的唯一来源）。</summary>
        public FlowNodeMetrics Metrics = new FlowNodeMetrics();

        /// <summary>结果图形（图像坐标）；超过 <see cref="MaxShapes"/> 时截断并置 <see cref="ShapesTruncated"/>。</summary>
        public List<IGeometryShape> Shapes = new List<IGeometryShape>();

        /// <summary>辅助图形（ROI 轮廓 / 卡尺窗口）；<strong>不进结果区明细</strong>，只在画布上画。</summary>
        public List<IGeometryShape> AuxShapes = new List<IGeometryShape>();

        /// <summary>逐目标结果（含形状特征）；表格优先用它，拿不到再回退到 Shapes 的描述。</summary>
        public List<RegionInfo> Regions = new List<RegionInfo>();

        /// <summary>图形是否被截断（界面据此提示"仅显示前 N 条"）。</summary>
        public bool ShapesTruncated;

        /// <summary>节点自身摘要（日志同源）。</summary>
        public string Summary = "";

        /// <summary>
        /// 1/4 缩略帧；null = 没有（该节点还没跑过，或取不到源帧）。
        /// <strong>仅供历史回看时显示与高亮，不参与任何计算。</strong>
        /// </summary>
        public Bitmap Thumbnail;

        /// <summary>缩略帧相对原图的缩小倍数（默认 <see cref="ThumbGeometry.ThumbScale"/>）。</summary>
        public int ThumbDivisor = ThumbGeometry.ThumbScale;

        /// <summary>原图宽（缩略帧 × 倍数；用于确认换算方向）。</summary>
        public int SourceWidth;

        /// <summary>原图高。</summary>
        public int SourceHeight;

        /// <summary>释放缩略帧（调用后 <see cref="Thumbnail"/> 为 null）。</summary>
        public void ReleaseThumbnail()
        {
            if (Thumbnail != null)
            {
                Thumbnail.Dispose();
                Thumbnail = null;
            }
        }
    }

    /// <summary>
    /// 缩略帧与原图之间的坐标换算（<strong>唯一实现</strong>）。
    /// <para>
    /// <strong>红线</strong>：缩放后的图形一律 <c>IsSelected = false</c> 落地，高亮只能由
    /// <c>ImageViewer.SelectShape()</c> 施加（它会对全部图形执行单选判定）。
    /// 任何"直接设 <c>IsSelected = true</c>"的写法都会让"两个目标同时高亮"变成可能，
    /// 而这正是 C5-b/C5-c 要防的"越点越多"。
    /// </para>
    /// <para>
    /// 【换算方向】结果图形一律存<strong>原图坐标</strong>；显示在 1/4 缩略帧上必须
    /// <strong>除以 <see cref="ThumbScale"/></strong>。反了（×4）会让框飞出画面，且不报错。
    /// </para>
    /// <para>
    /// 【取整规则】不用 <c>Math.Round</c>（会引入 ±0.5 抖动，使得同一个框在两次换算里位置不同），
    /// 一律用<strong>浮点除法不取整</strong> —— 图形的绘制本来就支持浮点坐标，四舍五入纯属自找误差。
    /// 只有需要像素索引时（测试/断言）才 <c>Math.Round</c> 到整数。
    /// </para>
    /// </summary>
    public static class ThumbGeometry
    {
        /// <summary>
        /// 缩略帧缩小倍数（4 = 1/4 尺寸，面积 1/16）。<strong>只此一处</strong>，
        /// 将来要 1/2 或 1/8 只改这个数（绘制侧的图形尺寸必须与它保持一致）。
        /// </summary>
        public const int ThumbScale = 4;

        /// <summary>
        /// 原图长度 → 缩略长度：<c>round(v / ThumbScale)</c>，取整用
        /// <see cref="MidpointRounding.AwayFromZero"/>。
        /// <para>
        /// 【为什么不用 <c>(int)</c> 截断】截断是"永远向零取整"，会给所有坐标引入一个
        /// <strong>单向 ≤1 缩略像素的系统偏移</strong>，表现就是"框整体偏左上" ——
        /// 这是最难解释的一类视觉问题（看起来像取样错位，实际是取整方向错）。
        /// </para>
        /// </summary>
        public static float ToThumb(float original)
        {
            return ToThumb(original, ThumbScale);
        }

        /// <summary>按任意倍数换算（<paramref name="divisor"/> = 1 时即"只克隆不改几何"）。</summary>
        public static float ToThumb(float original, float divisor)
        {
            if (divisor <= 0f) divisor = ThumbScale;
            return (float)Math.Round(original / (double)divisor, MidpointRounding.AwayFromZero);
        }

        /// <summary>缩略长度 → 原图长度（<strong>仅供断言/诊断</strong>；绘制链路禁止使用）。</summary>
        public static float ToOriginal(float thumb)
        {
            return thumb * ThumbScale;
        }

        /// <summary>原图坐标点 → 缩略坐标点。</summary>
        public static PointF ToThumb(PointF p)
        {
            return new PointF(ToThumb(p.X), ToThumb(p.Y));
        }

        /// <summary>原图矩形 → 缩略矩形。</summary>
        public static RectangleF ToThumb(RectangleF r)
        {
            return new RectangleF(ToThumb(r.X), ToThumb(r.Y), ToThumb(r.Width), ToThumb(r.Height));
        }

        /// <summary>
        /// 把图形整体缩到缩略帧坐标系，返回<strong>新实例</strong>（不改原对象 —— 原对象还挂在
        /// 缓存里供结果区读数值，就地改会把"原图坐标"这个契约破坏掉）。
        /// <para>未知类型返回 null：宁可不画高亮，也不要画错位置。</para>
        /// </summary>
        public static IGeometryShape ScaleShape(IGeometryShape shape)
        {
            return ScaleShape(shape, ThumbScale);
        }

        /// <summary>
        /// 按 <paramref name="divisor"/> 生成副本（<c>1</c> = 只克隆、几何不变，供"改标签再画"用）。
        /// </summary>
        public static IGeometryShape ScaleShape(IGeometryShape shape, float divisor)
        {
            if (shape == null) return null;
            Func<float, float> f = delegate (float v) { return ToThumb(v, divisor); };

            var rect = shape as RectangleShape;
            if (rect != null)
            {
                return new RectangleShape(f(rect.X), f(rect.Y), f(rect.Width), f(rect.Height))
                {
                    StrokeColor = rect.StrokeColor,
                    LineWidth = rect.LineWidth,
                    IsEditable = false,
                    Label = rect.Label,
                    Name = rect.Name
                };
            }

            var rr = shape as RotatedRectShape;
            if (rr != null)
            {
                return new RotatedRectShape(new PointF(f(rr.Center.X), f(rr.Center.Y)), f(rr.Width), f(rr.Height), rr.AngleDeg)
                {
                    StrokeColor = rr.StrokeColor,
                    LineWidth = rr.LineWidth,
                    IsEditable = false,
                    Label = rr.Label,
                    Name = rr.Name
                };
            }

            var line = shape as LineSegmentShape;
            if (line != null)
            {
                return new LineSegmentShape(new PointF(f(line.P1.X), f(line.P1.Y)), new PointF(f(line.P2.X), f(line.P2.Y)))
                {
                    StrokeColor = line.StrokeColor,
                    LineWidth = line.LineWidth,
                    IsEditable = false,
                    Name = line.Name
                };
            }

            var ring = shape as RingShape;
            if (ring != null)
            {
                return new RingShape(new PointF(f(ring.Center.X), f(ring.Center.Y)), f(ring.OuterRadius), f(ring.InnerRadius))
                {
                    StrokeColor = ring.StrokeColor,
                    LineWidth = ring.LineWidth,
                    IsEditable = false,
                    Name = ring.Name
                };
            }

            var circle = shape as CircleShape;
            if (circle != null)
            {
                return new CircleShape(new PointF(f(circle.Center.X), f(circle.Center.Y)), f(circle.Radius))
                {
                    StrokeColor = circle.StrokeColor,
                    LineWidth = circle.LineWidth,
                    IsEditable = false,
                    Name = circle.Name
                };
            }

            var marker = shape as PointMarkerShape;
            if (marker != null)
            {
                // 点标记的 Size 是屏幕像素、不随缩放变，故只缩中心不缩 Size
                return new PointMarkerShape(new PointF(f(marker.Center.X), f(marker.Center.Y)))
                {
                    Size = marker.Size,
                    StrokeColor = marker.StrokeColor,
                    IsEditable = false,
                    Name = marker.Name
                };
            }

            return null;
        }

        /// <summary>
        /// 从一张源帧生成 1/4 缩略帧（<strong>24bpp</strong>，避免 8bpp 索引图后续无法建 Graphics）。
        /// <para>用 <see cref="InterpolationMode.Bilinear"/>：缩略图只用于"看框大概在哪"，
        /// 双线性足够且比 HighQualityBicubic 快数倍（连续运行时每节点每 400ms 会调一次）。</para>
        /// </summary>
        public static Bitmap BuildThumbnail(Bitmap source)
        {
            if (source == null || source.Width <= 0 || source.Height <= 0) return null;

            int w = Math.Max(1, source.Width / ThumbScale);
            int h = Math.Max(1, source.Height / ThumbScale);

            var thumb = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            try
            {
                using (var g = Graphics.FromImage(thumb))
                {
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.InterpolationMode = InterpolationMode.Bilinear;
                    g.DrawImage(source, new Rectangle(0, 0, w, h));
                }
                return thumb;
            }
            catch (Exception)
            {
                thumb.Dispose();
                throw;
            }
        }
    }
}
