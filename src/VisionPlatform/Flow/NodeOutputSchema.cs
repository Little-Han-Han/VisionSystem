using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 一个<strong>带类型</strong>的输出值（节点的端口数据）。
    /// <para>
    /// 【为什么不能全用字符串】"12.5"到底是 12.5 毫米、12.5 度，还是坐标 (12.5, 0)？
    /// 光看字符串分不出；脚本里也没法直接参与算术，还得自己 Parse。
    /// 这里给每个值带上 <see cref="Kind"/>，格式化交给 <see cref="Format"/>：
    /// 角度带 °、坐标带括号、OK/NG 用文字，脚本按类型取值（Number / Angle / Point / Bool…）。
    /// </para>
    /// </summary>
    public sealed class PortValue
    {
        /// <summary>稳定键名（脚本里按它取值，如 "TopScore" / "Cx" / "Angle"）。</summary>
        public string Key = "";

        /// <summary>中文显示名（结果显示区用）。</summary>
        public string DisplayName = "";

        /// <summary>数据类型。</summary>
        public FlowPortKind Kind = FlowPortKind.Number;

        /// <summary>原始值：double / bool / string / PointF / RectangleF。</summary>
        public object Raw;

        public PortValue() { }

        public PortValue(string key, string displayName, FlowPortKind kind, object raw)
        {
            Key = key ?? "";
            DisplayName = string.IsNullOrEmpty(displayName) ? Key : displayName;
            Kind = kind;
            Raw = raw;
        }

        /// <summary>取数值（非数值类型返回 <paramref name="fallback"/>）。</summary>
        public double AsNumber(double fallback)
        {
            if (Raw == null) return fallback;
            try
            {
                if (Raw is bool) return ((bool)Raw) ? 1.0 : 0.0;
                return Convert.ToDouble(Raw, CultureInfo.InvariantCulture);
            }
            catch (Exception) { return fallback; }
        }

        /// <summary>取布尔（NG/0/空 视为 false）。</summary>
        public bool AsBool(bool fallback)
        {
            if (Raw == null) return fallback;
            if (Raw is bool) return (bool)Raw;

            double d;
            if (double.TryParse(Convert.ToString(Raw, CultureInfo.InvariantCulture),
                                NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return Math.Abs(d) > 1e-9;

            string s = Convert.ToString(Raw, CultureInfo.InvariantCulture) ?? "";
            if (s.Equals("OK", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("NG", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        /// <summary>把坐标类值格式化成 <c>(x, y)</c>；不是坐标时退回原值的字符串。</summary>
        private string AsPointText()
        {
            if (Raw is PointF)
            {
                var p = (PointF)Raw;
                return "(" + p.X.ToString("F1", CultureInfo.InvariantCulture)
                     + ", " + p.Y.ToString("F1", CultureInfo.InvariantCulture) + ")";
            }
            return Convert.ToString(Raw, CultureInfo.InvariantCulture);
        }

        /// <summary>按类型格式化：角度 12.34°、坐标 (320.0, 240.0)、布尔 OK/NG。</summary>
        public string Format()
        {
            if (Raw == null) return "";

            switch (Kind)
            {
                case FlowPortKind.Angle:
                    return AsNumber(0).ToString("F2", CultureInfo.InvariantCulture) + "°";

                case FlowPortKind.Point:
                    {
                        var p = Raw as PointF?;
                        if (p.HasValue)
                            return "(" + p.Value.X.ToString("F1", CultureInfo.InvariantCulture)
                                 + ", " + p.Value.Y.ToString("F1", CultureInfo.InvariantCulture) + ")";
                        return Convert.ToString(Raw, CultureInfo.InvariantCulture);
                    }

                case FlowPortKind.Rect:
                    {
                        var r = Raw as RectangleF?;
                        if (r.HasValue)
                            return "(" + r.Value.X.ToString("F0", CultureInfo.InvariantCulture)
                                 + ", " + r.Value.Y.ToString("F0", CultureInfo.InvariantCulture)
                                 + ", " + r.Value.Width.ToString("F0", CultureInfo.InvariantCulture)
                                 + "×" + r.Value.Height.ToString("F0", CultureInfo.InvariantCulture) + ")";
                        return Convert.ToString(Raw, CultureInfo.InvariantCulture);
                    }

                case FlowPortKind.Bool:
                    return AsBool(false) ? "OK" : "NG";

                case FlowPortKind.Pose:
                    return AsPointText();

                case FlowPortKind.Geometry:
                    {
                        var g = Raw as FlowGeometryValue;
                        return g != null ? g.ToString() : Convert.ToString(Raw, CultureInfo.InvariantCulture);
                    }

                case FlowPortKind.Number:
                    {
                        double d = AsNumber(double.NaN);
                        if (double.IsNaN(d)) return Convert.ToString(Raw, CultureInfo.InvariantCulture);
                        return d.ToString("0.###", CultureInfo.InvariantCulture);
                    }

                default:
                    return Convert.ToString(Raw, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>类型显示名（脚本编辑器 / 结果显示用）。</summary>
        public string KindName
        {
            get
            {
                switch (Kind)
                {
                    case FlowPortKind.Image: return "图像";
                    case FlowPortKind.Pose: return "位姿";
                    case FlowPortKind.Point: return "坐标";
                    case FlowPortKind.Angle: return "角度";
                    case FlowPortKind.Number: return "数值";
                    case FlowPortKind.Bool: return "判定";
                    case FlowPortKind.Text: return "文本";
                    case FlowPortKind.Rect: return "矩形";
                    case FlowPortKind.Geometry: return "几何";
                    case FlowPortKind.BlobList: return "列表";
                    case FlowPortKind.PointCloud: return "点云";
                    default: return "任意";
                }
            }
        }
    }

    /// <summary>
    /// 按节点类型收集"这个节点输出了什么"——<strong>同一份定义同时喂给结果显示与脚本</strong>，
    /// 避免"界面上写 12.5°、脚本里读到 12.5"这种两套口径。
    /// </summary>
    public static class NodeOutputSchema
    {
        /// <summary>按节点类型 + 运行指标，产出该节点的类型化输出列表。</summary>
        public static List<PortValue> Build(FlowNodeType type, FlowNodeMetrics m, bool ok)
        {
            var list = new List<PortValue>();
            if (m == null) m = new FlowNodeMetrics();

            // 判定值所有节点都有
            list.Add(new PortValue("Ok", "判定", FlowPortKind.Bool, ok));

            switch (type)
            {
                case FlowNodeType.ShapeMatch:
                case FlowNodeType.TemplateMatch:
                    AddNumber(list, "Count", "目标数", m.TargetCount);
                    AddNumber(list, "Score", "最高分", m.TopScore);
                    AddPoint(list, "Center", "中心", m.TopCx, m.TopCy);
                    AddAngle(list, "Angle", "角度", m.AngleDeg);
                    break;

                case FlowNodeType.Blob:
                    AddNumber(list, "Count", "目标数", m.TargetCount);
                    AddNumber(list, "MaxArea", "最大面积", m.MaxArea);
                    AddNumber(list, "Circularity", "圆度", m.MaxCircularity);
                    break;

                case FlowNodeType.Ocr:
                    AddNumber(list, "Count", "字符数", m.CharCount);
                    AddNumber(list, "Score", "平均置信度", m.MeanScore);
                    if (!string.IsNullOrEmpty(m.Text))
                        list.Add(new PortValue("Text", "文本", FlowPortKind.Text, m.Text));
                    break;

                case FlowNodeType.LineCaliper:
                case FlowNodeType.CircleCaliper:
                    AddPoint(list, "Center", "中心", m.TopCx, m.TopCy);
                    AddAngle(list, "Angle", "角度", m.AngleDeg);
                    break;

                case FlowNodeType.PositionCorrect:
                    AddAngle(list, "Angle", "角度偏差", m.AngleDeg);
                    break;

                case FlowNodeType.Script:
                    if (!string.IsNullOrEmpty(m.Text))
                        list.Add(new PortValue("Text", "脚本结果", FlowPortKind.Text, m.Text));
                    break;

                case FlowNodeType.PointCloudSource:
                    AddNumber(list, "Count", "点数", m.TargetCount);
                    break;
            }

            return list;
        }

        private static void AddNumber(List<PortValue> list, string key, string name, int value)
        {
            if (value < 0) return;                        // -1 = 不适用
            list.Add(new PortValue(key, name, FlowPortKind.Number, (double)value));
        }

        private static void AddNumber(List<PortValue> list, string key, string name, double value)
        {
            if (double.IsNaN(value)) return;              // NaN = 不适用
            list.Add(new PortValue(key, name, FlowPortKind.Number, value));
        }

        private static void AddAngle(List<PortValue> list, string key, string name, double value)
        {
            if (double.IsNaN(value)) return;
            list.Add(new PortValue(key, name, FlowPortKind.Angle, value));
        }

        private static void AddPoint(List<PortValue> list, string key, string name, double x, double y)
        {
            if (double.IsNaN(x) || double.IsNaN(y)) return;
            list.Add(new PortValue(key, name, FlowPortKind.Point,
                new PointF((float)x, (float)y)));
        }

        /// <summary>一行摘要文本：`判定 OK · 目标数 5 · 最高分 0.981 · 中心 (320.0, 240.0) · 角度 12.34°`。</summary>
        public static string Describe(IEnumerable<PortValue> values)
        {
            if (values == null) return "";

            var sb = new StringBuilder();
            foreach (PortValue v in values)
            {
                if (v == null) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(v.DisplayName).Append(' ').Append(v.Format());
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 一个检测目标（Blob / 圆 / 匹配）的几何与测量值。
    /// <para>
    /// 【为什么要单独一个结构】汇总值（最大面积、最高分）回答不了"每个目标分别是什么样"；
    /// 脚本要做分级判定、要把第 3 个目标的坐标传给下游，就必须拿到<strong>逐个目标</strong>的数据。
    /// </para>
    /// </summary>
    public sealed class RegionInfo
    {
        /// <summary>序号（从 1 开始）。</summary>
        public int Index;
        /// <summary>中心 X。</summary>
        public double Cx;
        /// <summary>中心 Y。</summary>
        public double Cy;
        /// <summary>面积（像素²）。</summary>
        public double Area;
        /// <summary>宽。</summary>
        public double Width;
        /// <summary>高。</summary>
        public double Height;
        /// <summary>角度（度）。</summary>
        public double Angle;
        /// <summary>圆度（4πA/P²）。</summary>
        public double Circularity;
        /// <summary>附加文本（OCR 文本 / 类别名等）。</summary>
        public string Text = "";

        /// <summary>一行摘要：`#1 (320.0, 240.0) 40×30 A=1200 C=0.86`。</summary>
        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "#{0} ({1:F1}, {2:F1}) {3:F0}×{4:F0} A={5:F0} C={6:F2}",
                Index, Cx, Cy, Width, Height, Area, Circularity);
        }
    }
}
