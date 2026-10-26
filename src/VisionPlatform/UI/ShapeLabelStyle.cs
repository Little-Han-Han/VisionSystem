using System;
using System.Globalization;
using VisionPlatform.Core;
using VisionSystem.Controls.Geometry;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 画布叠加标签的<strong>三档详细度</strong>渲染（UI-R04 / Q17）。
    /// <para>
    /// 【为什么在 UI 层做，而不在 FlowRunner 里生成三套标签】详细度是<strong>看图的偏好</strong>，
    /// 不是检测结果的一部分：写进结果会让"同一帧的两次运行结果不同"，也会让日志/报表随界面设置漂移。
    /// 因此 runner 只产出完整的 <c>#序号 分数</c>，这里在<strong>送进画面之前</strong>按档位裁剪/加长。
    /// </para>
    /// <para>
    /// 【必须克隆】结果图形实例同时被结果区缓存持有（明细行读它的 <c>Label</c> 与几何）。
    /// 就地改 Label 会把结果区文字一起改掉 —— 所以只有"需要改变文字"的档位才克隆，标准档直接用原实例（零分配）。
    /// </para>
    /// </summary>
    public static class ShapeLabelStyle
    {
        /// <summary>当前档位（1 简 / 2 标准 / 3 详细），来自 <see cref="UiSettings.FlowLabelDetail"/>。</summary>
        public static int Current { get; set; }

        static ShapeLabelStyle()
        {
            Current = UiSettings.LabelDetailStandard;
        }

        /// <summary>从配置刷新当前档位（窗体构造 / 设置变更时调用）。</summary>
        public static void Reload()
        {
            AppConfig cfg = ConfigStore.Current;
            Current = (cfg != null && cfg.Ui != null)
                ? UiSettings.NormalizeLabelDetail(cfg.Ui.FlowLabelDetail)
                : UiSettings.LabelDetailStandard;
        }

        /// <summary>档位中文名（按钮文案用）。</summary>
        public static string Name(int level)
        {
            switch (UiSettings.NormalizeLabelDetail(level))
            {
                case UiSettings.LabelDetailSimple: return "简";
                case UiSettings.LabelDetailDetailed: return "详细";
                default: return "标准";
            }
        }

        /// <summary>
        /// 按当前档位生成"可画"的图形：标准档返回原实例，其余档返回带改写标签的副本。
        /// </summary>
        public static IGeometryShape Render(IGeometryShape shape, int level)
        {
            if (shape == null) return null;

            string label = GetLabel(shape);
            if (string.IsNullOrEmpty(label)) return shape;

            int lv = UiSettings.NormalizeLabelDetail(level);
            if (lv == UiSettings.LabelDetailStandard) return shape;

            string next = (lv == UiSettings.LabelDetailSimple) ? Shorten(label) : Extend(shape, label);
            if (string.Equals(next, label, StringComparison.Ordinal)) return shape;

            IGeometryShape copy = ThumbGeometry.ScaleShape(shape, 1f);   // 只克隆，几何不变
            if (copy == null) return shape;
            SetLabel(copy, next);
            copy.Name = shape.Name;
            return copy;
        }

        /// <summary>
        /// 读标签。<see cref="IGeometryShape"/> 接口上没有 <c>Label</c>（它只加在矩形/旋转矩形上，
        /// 因为只有这两类会带标签），故按具体类型取；其它类型直接视为"无标签"。
        /// </summary>
        private static string GetLabel(IGeometryShape shape)
        {
            var rect = shape as RectangleShape;
            if (rect != null) return rect.Label;

            var rr = shape as RotatedRectShape;
            if (rr != null) return rr.Label;

            return null;
        }

        /// <summary>写标签（与 <see cref="GetLabel"/> 对称）。</summary>
        private static void SetLabel(IGeometryShape shape, string label)
        {
            var rect = shape as RectangleShape;
            if (rect != null) { rect.Label = label; return; }

            var rr = shape as RotatedRectShape;
            if (rr != null) rr.Label = label;
        }

        /// <summary>"#1 0.93" → "#1"：只留第一个空格之前的部分。</summary>
        private static string Shorten(string label)
        {
            int i = label.IndexOf(' ');
            return i > 0 ? label.Substring(0, i) : label;
        }

        /// <summary>"#1 0.93" → "#1 0.93 · (412,208) 86×64 [12.0°]"。</summary>
        private static string Extend(IGeometryShape shape, string label)
        {
            var rect = shape as RectangleShape;
            if (rect != null)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} · ({1:0},{2:0}) {3:0}×{4:0}",
                    label, rect.X, rect.Y, rect.Width, rect.Height);
            }

            var rr = shape as RotatedRectShape;
            if (rr != null)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} · ({1:0},{2:0}) {3:0}×{4:0} {5:0.0}°",
                    label, rr.Center.X, rr.Center.Y, rr.Width, rr.Height, rr.AngleDeg);
            }

            var circle = shape as CircleShape;
            if (circle != null)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} · C=({1:0},{2:0}) R={3:0}",
                    label, circle.Center.X, circle.Center.Y, circle.Radius);
            }

            return label;
        }
    }
}
