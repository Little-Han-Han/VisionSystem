using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>预览区可手动画出的几何类型（对应不同卡尺 / ROI 参数）。</summary>
    public enum DrawShapeKind
    {
        /// <summary>轴对齐矩形 ROI（X / Y / W / H）。</summary>
        Rect,

        /// <summary>直线卡尺的扫描线段（X1 / Y1 / X2 / Y2）。</summary>
        Line,

        /// <summary>圆卡尺的环形 ROI（Cx / Cy / InnerRadius / OuterRadius）。</summary>
        Ring
    }

    /// <summary>
    /// 预览区「画几何」的一组参数：要画什么形状、用哪种交互模式、显示名、历史主键，
    /// 以及写回节点的四参数键（按 <see cref="Kind"/> 解释前四个键的含义）。
    /// <para>
    /// 不硬编码节点类型：由 <see cref="FindDrawSpecs"/> 按参数表实际字段匹配生成，
    /// 哪个节点真的有这些键就对它可用。矩形适用于模板区域 / ROI 裁剪 / 搜索区域；
    /// 直线与圆环分别适用于直线卡尺 / 圆卡尺的扫描几何。
    /// </para>
    /// </summary>
    public sealed class DrawSpec
    {
        /// <summary>几何类型。</summary>
        public readonly DrawShapeKind Kind;

        /// <summary>进入绘制时控件应切换到的交互模式。</summary>
        public readonly InteractionMode Mode;

        /// <summary>菜单 / 提示里显示的名称（一般取参数表分组标题）。</summary>
        public readonly string Label;

        /// <summary>历史选择用的稳定主键（矩形=该组的 X 键；直线=X1；圆环=Cx）。</summary>
        public readonly string PrimaryKey;

        /// <summary>进入绘制时已有的几何（用于预览）；可能为 null。</summary>
        public IGeometryShape Current;

        // 写回键：含义随 Kind 而定
        public readonly string X, Y, W, H;                 // Rect: X / Y / W / H
        public readonly string X1, Y1, X2, Y2;             // Line: 线段两端点
        public readonly string Cx, Cy, InnerRadius, OuterRadius; // Ring: 圆心与内外半径

        private DrawSpec(DrawShapeKind kind, InteractionMode mode, string label, string primaryKey,
            string x, string y, string w, string h,
            string x1, string y1, string x2, string y2,
            string cx, string cy, string inner, string outer)
        {
            Kind = kind; Mode = mode; Label = label; PrimaryKey = primaryKey;
            X = x; Y = y; W = w; H = h;
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
            Cx = cx; Cy = cy; InnerRadius = inner; OuterRadius = outer;
        }

        /// <summary>构造矩形 ROI 描述（模板区域 / ROI 裁剪 / 搜索区域）。</summary>
        public static DrawSpec MakeRect(string label, string x, string y, string w, string h)
        {
            return new DrawSpec(DrawShapeKind.Rect, InteractionMode.DrawRectangle, label, x,
                x, y, w, h, null, null, null, null, null, null, null, null);
        }

        /// <summary>构造直线卡尺描述（扫描线段 X1/Y1/X2/Y2）。</summary>
        public static DrawSpec MakeLine(string label)
        {
            return new DrawSpec(DrawShapeKind.Line, InteractionMode.DrawLine, label, "X1",
                null, null, null, null, "X1", "Y1", "X2", "Y2", null, null, null, null);
        }

        /// <summary>构造圆卡尺描述（环形 ROI Cx/Cy/InnerRadius/OuterRadius）。</summary>
        public static DrawSpec MakeRing(string label)
        {
            return new DrawSpec(DrawShapeKind.Ring, InteractionMode.DrawRing, label, "Cx",
                null, null, null, null, null, null, null, null, "Cx", "Cy", "InnerRadius", "OuterRadius");
        }
    }
}
