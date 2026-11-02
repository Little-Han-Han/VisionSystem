using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VisionPlatform.Calibration
{
    /// <summary>从通讯回复里解析出来的机械坐标。</summary>
    public struct CalibCoord
    {
        /// <summary>机械 X（mm）。</summary>
        public double X;

        /// <summary>机械 Y（mm）。</summary>
        public double Y;

        /// <summary>旋转轴 U / θ（度）。仅当回复里给了第三个数值时有效。</summary>
        public double U;

        /// <summary>回复里是否解析到第三个数值（U / θ）。</summary>
        public bool HasU;

        /// <summary>界面用的紧凑文本。</summary>
        public string Display
        {
            get
            {
                string text = "X=" + X.ToString("F4", CultureInfo.InvariantCulture)
                              + "  Y=" + Y.ToString("F4", CultureInfo.InvariantCulture);
                if (HasU) text += "  U=" + U.ToString("F4", CultureInfo.InvariantCulture);
                return text;
            }
        }
    }

    /// <summary>
    /// 把 PLC / 运动控制卡返回的一行文本解析成机械坐标。
    /// <para>
    /// 【为什么用正则抽数字】现场返回格式五花八门（<c>"1.2,3.4,90"</c> /
    /// <c>"POS:1.2 3.4 90.0"</c> / <c>"X1.2 Y3.4 U90\r\n"</c>），
    /// 逐个指令模板写解析器不现实。统一抽取「可选的符号 + 数字 + 可选小数 / 指数」，
    /// 取前 2~3 个数值即可，模板文本、单位、前缀都被自然跳过。
    /// </para>
    /// <para>
    /// 【失败必须可读】解析不出足够数值时返回中文原因，界面用它提示用户，
    /// 绝不静默失败（现场排查成本最高的就是"点了没反应"）。
    /// </para>
    /// </summary>
    public static class CalibCoordReply
    {
        /// <summary>数值抽取：支持 1 / -1.5 / +2. / 1e-3 等写法。</summary>
        private static readonly Regex NumberPattern = new Regex(
            @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?",
            RegexOptions.CultureInvariant);

        /// <summary>最多需要的数值个数（X / Y / U）。</summary>
        private const int MaxNumbers = 3;

        /// <summary>
        /// 尝试从一行回复里解析机械坐标。
        /// </summary>
        /// <param name="reply">通讯收到的原始文本。</param>
        /// <param name="coord">解析结果（成功时有效）。</param>
        /// <param name="error">失败原因（成功时为空串）。</param>
        /// <returns>解析到 ≥2 个数值返回 true（第 3 个数值存在时填入 U）。</returns>
        public static bool TryParse(string reply, out CalibCoord coord, out string error)
        {
            coord = new CalibCoord();
            error = "";

            if (string.IsNullOrEmpty(reply))
            {
                error = "通讯回复为空";
                return false;
            }

            List<double> values = new List<double>(MaxNumbers);
            foreach (Match match in NumberPattern.Matches(reply))
            {
                double value;
                if (!double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    continue;

                values.Add(value);
                if (values.Count >= MaxNumbers) break;
            }

            if (values.Count < 2)
            {
                error = "回复里未找到 2 个以上的数值（原始内容：" + Shorten(reply) + "）";
                return false;
            }

            coord.X = values[0];
            coord.Y = values[1];
            coord.HasU = values.Count >= 3;
            coord.U = coord.HasU ? values[2] : 0.0;
            return true;
        }

        /// <summary>把超长回复截断，避免消息框被灌满。</summary>
        private static string Shorten(string text)
        {
            string trimmed = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return trimmed.Length <= 60 ? trimmed : trimmed.Substring(0, 60) + "…";
        }
    }
}
