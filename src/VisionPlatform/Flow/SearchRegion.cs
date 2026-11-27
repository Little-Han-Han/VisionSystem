using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 搜索区域的形状。
    /// <para>
    /// 【为什么先只有矩形】非矩形区域不是"参数多一个枚举"那么简单：裁剪出来的图是矩形的，
    /// 圆/多边形之外的像素必须靠**掩膜**告诉算法"这块不算"，否则圆形 ROI 退化成一个外接框。
    /// 掩膜要一路传到原生库（PaDiM 的逐位置统计、NCC 的归一化、Blob 的连通域都得按掩膜算），
    /// 属于下一阶段的改造。本枚举先把位置占住，参数表暂不暴露未实现的选项。
    /// </para>
    /// </summary>
    public enum SearchRegionKind
    {
        /// <summary>矩形（可带自身角度）。</summary>
        Rect = 0,

        // Circle  = 1,   // 预留：圆形 / 椭圆，需要 native 掩膜
        // Polygon = 2,   // 预留：多边形，需要 native 掩膜
    }

    /// <summary>
    /// <strong>搜索区域的唯一入口</strong>——所有算法要"在整帧的某个子区域里干活"都必须走这里。
    /// <para>
    /// 【统一之前是什么样子】同一件事有三套实现、两套参数命名：
    /// <list type="bullet">
    /// <item><c>RoiX/Y/W/H</c> + <c>CropRoi</c>（真仿射旋正）—— 异常检测、字符识别；</item>
    /// <item><c>RoiX/Y/W/H</c> + <c>ResolveRoi</c>（只取外接框）—— 图像滤波、颜色提取；</item>
    /// <item><c>SearchX/Y/W/H</c> + <c>ApplyRoi</c>（只取外接框）—— 形状匹配、模板匹配。</item>
    /// </list>
    /// 于是"位置修正带旋转时怎么办"每个节点的答案都不一样，现场看到的就是
    /// 「有的框跟着转、有的不转」「同一块 ROI 在两个节点里覆盖的范围不同」。
    /// </para>
    /// <para>
    /// 【统一成什么】<b>读取参数</b>和<b>坐标换算</b>收敛到本类；<b>怎么用</b>由各算法自己决定，
    /// 因为语义本来就不同，硬统一反而错：
    /// <list type="bullet">
    /// <item><see cref="Crop"/>（旋正）—— 逐位置比对类：异常检测、字符识别；</item>
    /// <item><see cref="BoundsToFrame"/>（外接框）—— 需要"贴回原图"或内部自带角度搜索的：
    ///   图像滤波、形状匹配、模板匹配；</item>
    /// <item><see cref="ContainsPoint"/>（精确判定）—— 只做点过滤的：颜色提取。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 【参数前缀兼容】历史节点用 <c>Search*</c>，新节点用 <c>Roi*</c>。读取时优先 <c>Roi*</c>、
    /// 回退 <c>Search*</c>，因此老方案不用迁移；新节点一律只写 <c>Roi*</c>。
    /// </para>
    /// </summary>
    public sealed class SearchRegion
    {
        /// <summary>形状。目前恒为 <see cref="SearchRegionKind.Rect"/>。</summary>
        public SearchRegionKind Kind { get; private set; }

        /// <summary>
        /// 示教坐标下的矩形（<b>已减去裁剪偏移，未应用位置修正</b>）。宽或高 ≤ 0 表示"整幅"。
        /// </summary>
        public Rectangle Rect { get; private set; }

        /// <summary>ROI 自身角度（度，顺时针为正，绕矩形中心）。0 = 正放。</summary>
        public double AngleDeg { get; private set; }

        /// <summary>是否"不限制区域"（宽或高 ≤ 0）。</summary>
        public bool IsEmpty { get { return Rect.Width <= 0 || Rect.Height <= 0; } }

        private SearchRegion() { }

        /// <summary>整幅（不做任何限制）。</summary>
        public static SearchRegion Whole()
        {
            return new SearchRegion { Kind = SearchRegionKind.Rect, Rect = Rectangle.Empty };
        }

        /// <summary>
        /// 从节点参数读取——<strong>所有算法的唯一入口</strong>。
        /// <para>不允许再各自写 <c>node.GetInt("RoiW")</c> / <c>ApplyRoi(..., "SearchW", ...)</c>，
        /// 否则"多一套实现"的老问题会立刻回来。</para>
        /// </summary>
        /// <param name="node">节点。</param>
        /// <param name="offsetX">裁图偏移（流程里是 <c>s.OffsetX</c>）。</param>
        /// <param name="offsetY">裁图偏移。</param>
        /// <param name="useSearchKeys">
        /// 读 <c>SearchX/Y/W/H</c> 而不是 <c>RoiX/Y/W/H</c>。
        /// <para>
        /// 【为什么必须显式指定，不能自动回退】最初写成"Roi* 读不到就回退 Search*"，
        /// 结果原本只读 Roi* 的节点（图像滤波）串读到了别的 key，拿到一个越界矩形，
        /// 贴回整帧时抛"参数无效"。参数名的选择必须由调用方声明，不能靠猜。
        /// 目前只有形状匹配与模板匹配用 Search*（历史命名），其余一律 Roi*。
        /// </para>
        /// </param>
        public static SearchRegion FromNode(FlowNode node, double offsetX, double offsetY,
                                            bool useSearchKeys = false)
        {
            if (node == null) return Whole();

            string px = useSearchKeys ? "Search" : "Roi";
            int x = node.GetInt(px + "X", 0);
            int y = node.GetInt(px + "Y", 0);
            int w = node.GetInt(px + "W", 0);
            int h = node.GetInt(px + "H", 0);
            double ang = useSearchKeys ? 0.0 : node.GetDouble("RoiAngle", 0.0);

            // 【必须先判宽高再构造 Rectangle】宽或高为负时 new Rectangle(...) 自己就抛
            // ArgumentException("参数无效")，而且是在调用方 try 之外求值 —— 表现为
            // "节点执行异常（参数无效）"，堆栈里还看不到是谁。旧版 ResolveRoi 有同样的早退，
            // 统一到本类时漏掉了，被回归 T12b 抓到。
            if (w <= 0 || h <= 0) return Whole();

            return new SearchRegion
            {
                Kind = SearchRegionKind.Rect,
                Rect = new Rectangle((int)Math.Round(x - offsetX), (int)Math.Round(y - offsetY), w, h),
                AngleDeg = ang
            };
        }

        /// <summary>是否需要真仿射：ROI 自身带角度，或位置修正带旋转。</summary>
        public bool NeedsAffine(FlowRunTransform t)
        {
            return RoiAffineMap.NeedsAffine(t, AngleDeg);
        }

        /// <summary>生效角度（度）= ROI 自身角度 + 位置修正角度。</summary>
        public double EffectiveAngle(FlowRunTransform t)
        {
            return RoiAffineMap.EffectiveAngle(t, AngleDeg);
        }

        /// <summary>
        /// 整帧上的<b>轴对齐外接框</b>（已应用位置修正、已夹到图像内）。
        /// <para>给"只需要知道这块在哪"的算法用：图像滤波要按原位置贴回、形状/模板匹配内部
        /// 自己会搜角度（区域再旋转没有意义，反而让返回角度要多做一次换算）。</para>
        /// </summary>
        public Rectangle BoundsToFrame(FlowRunTransform t, int frameW, int frameH)
        {
            if (IsEmpty) return Rectangle.Empty;

            Rectangle user = (t != null) ? t.ApplyToRect(Rect) : Rect;

            int rx = Math.Max(0, Math.Min(user.X, Math.Max(0, frameW - 1)));
            int ry = Math.Max(0, Math.Min(user.Y, Math.Max(0, frameH - 1)));
            int rw = Math.Max(0, Math.Min(user.Width, frameW - rx));
            int rh = Math.Max(0, Math.Min(user.Height, frameH - ry));
            if (rw <= 0 || rh <= 0) return Rectangle.Empty;
            return new Rectangle(rx, ry, rw, rh);
        }

        /// <summary>
        /// 裁剪出<b>跟随工件姿态</b>的分析图：有旋转走真仿射旋正，否则整数裁剪。
        /// <para>逐位置比对类算法（异常检测、字符识别）必须用它 —— 取外接框会让图仍是斜的，
        /// 残余 5° 就足以让 256 网格上的特征错位 1.4 个 patch。</para>
        /// </summary>
        /// <param name="usedRect">
        /// 用于"局部坐标 → 整帧坐标"换算的矩形：无旋转时是夹过的外接框，有旋转时是示教矩形。
        /// 后续 <see cref="MapToFrame"/> 必须传它，不能传 <see cref="Rect"/>（未夹边界，会偏）。
        /// </param>
        /// <param name="affine">是否走了真仿射。坐标映射和热图回贴都要按它分支。</param>
        /// <returns>裁剪图；区域非法或越界时为 null。</returns>
        public Bitmap Crop(Bitmap frame, FlowRunTransform t, out Rectangle usedRect, out bool affine)
        {
            usedRect = Rectangle.Empty;
            affine = false;
            if (frame == null || IsEmpty) return null;

            affine = NeedsAffine(t);
            if (!affine)
            {
                usedRect = BoundsToFrame(t, frame.Width, frame.Height);
                return usedRect.IsEmpty ? null : CropPlain(frame, usedRect);
            }

            usedRect = Rect;
            return RoiAffineMap.Crop(frame, Rect, AngleDeg, t);
        }

        /// <summary>
        /// 裁剪图局部坐标 → 整帧坐标。
        /// </summary>
        /// <param name="usedRect"><see cref="Crop"/> 的 out 参数。</param>
        /// <param name="affine"><see cref="Crop"/> 的 out 参数。</param>
        public PointF MapToFrame(FlowRunTransform t, Rectangle usedRect, bool affine, double u, double v)
        {
            return RoiAffineMap.MapPoint(t, usedRect, AngleDeg, affine, u, v);
        }

        /// <summary>
        /// 整帧上的点是否落在区域内（精确判定，含旋转）。
        /// <para>做法是<b>逆变换回示教坐标再判</b>，而不是拿外接框判 —— 后者会让旋转 ROI 的
        /// 四个角多算进一大块，颜色提取的点过滤就会把区域外的目标也算进来。</para>
        /// <para>区域为空时返回 true（"不限制"的语义）。</para>
        /// </summary>
        public bool ContainsPoint(FlowRunTransform t, double frameX, double frameY)
        {
            if (IsEmpty) return true;
            if (t == null || !t.IsValid) return Rect.Contains((int)Math.Round(frameX), (int)Math.Round(frameY));

            PointF p = t.ApplyInverseToPoint(frameX, frameY);
            return Rect.Contains((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }

        /// <summary>
        /// 预览用的区域轮廓——画的是<b>实际生效的范围</b>，不是示教值。
        /// <para>
        /// 【为什么必须画生效范围】同一个区域在三种算法里的用法不同：旋正裁剪用的是"跟着工件转的矩形"，
        /// 外接框裁剪用的是"旋转后的轴对齐包围盒"。如果统一按示教矩形画，就会出现
        /// 「框画在一处、算法在另一处干活」——现场第一反应就是"搜索区域没生效"。
        /// </para>
        /// <para>
        /// 【为什么统一成带标签的矩形而不是四条边线】一个流程里往往有好几个节点各带搜索区域，
        /// 全画出来会重叠。带上"哪个节点 + 多大"，现场才能对上号；四条独立边线没地方挂标签。
        /// 顺带把 <c>IsEditable</c> 关掉——这是显示用的，拖一下就会改掉示教值。
        /// </para>
        /// </summary>
        /// <param name="affine">true = 画跟着工件转的矩形；false = 画整帧上的轴对齐外接框。</param>
        /// <param name="label">画在框上的文字（通常是节点名 + 尺寸）。</param>
        public List<IGeometryShape> Outline(FlowRunTransform t, int frameW, int frameH,
                                            bool affine, string label = null)
        {
            var list = new List<IGeometryShape>();

            // 区域为空（宽/高 = 0）在运行时语义是"整幅"，但界面上不能因此什么都不画 ——
            // 否则现场看到的是"这个节点根本没有搜索区域"，与"有区域但限制很松"完全分不清。
            // 这里画一个贴住整帧的框，标签注明「（全图）」，让两种状态都能一眼区分。
            if (IsEmpty)
            {
                if (frameW <= 0 || frameH <= 0) return list;
                var full = new RotatedRectShape(
                    new PointF(frameW / 2f, frameH / 2f), frameW, frameH, 0f);
                full.StrokeColor = FlowShapes.ColorRoi;
                full.LineWidth = FlowShapes.ThinLine;
                full.IsEditable = false;
                full.Name = FlowShapes.AuxContourName;
                full.Label = (string.IsNullOrEmpty(label) ? "搜索区域" : label) + "（全图）";
                list.Add(full);
                return list;
            }

            PointF center;
            float w, h, angle;
            if (!affine || t == null)
            {
                Rectangle bounds = BoundsToFrame(t, frameW, frameH);
                if (bounds.IsEmpty) return list;
                center = new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);
                w = bounds.Width;
                h = bounds.Height;
                angle = 0f;
            }
            else
            {
                center = RoiAffineMap.FrameCenter(t, Rect);
                w = Rect.Width;
                h = Rect.Height;
                angle = (float)EffectiveAngle(t);
            }

            var rr = new RotatedRectShape(center, w, h, angle);
            rr.StrokeColor = FlowShapes.ColorRoi;
            rr.LineWidth = FlowShapes.ThinLine;
            rr.IsEditable = false;
            rr.Name = FlowShapes.AuxContourName;
            if (!string.IsNullOrEmpty(label)) rr.Label = label;
            list.Add(rr);
            return list;
        }

        /// <summary>整数裁剪（保留原像素格式，逐像素一致）。</summary>
        private static Bitmap CropPlain(Bitmap src, Rectangle roi)
        {
            Rectangle real = Rectangle.Intersect(roi, new Rectangle(0, 0, src.Width, src.Height));
            if (real.Width <= 0 || real.Height <= 0) return null;
            return src.Clone(real, src.PixelFormat);
        }
    }
}
