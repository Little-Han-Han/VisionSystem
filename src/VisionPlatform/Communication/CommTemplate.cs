using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace VisionPlatform.Communication
{
    /// <summary>
    /// 消息模板渲染：<c>{Flow1.Passed}</c> / <c>{Line.Yield:F2}</c> / <c>{Time}</c>。
    /// <para>只匹配"标识符 + 可选格式"，JSON 里的引号/花括号不会误替换。</para>
    /// </summary>
    internal static class CommTemplate
    {
        private static readonly Regex Token = new Regex(
            @"\{([A-Za-z0-9_.]+)(?::([^}]+))?\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Render(string format, IDictionary<string, string> context)
        {
            if (string.IsNullOrEmpty(format)) return "";

            return Token.Replace(format, delegate(Match m)
            {
                string key = m.Groups[1].Value;
                string spec = m.Groups[2].Success ? m.Groups[2].Value : null;

                string value;
                if (context == null || !context.TryGetValue(key, out value))
                    return "";

                if (string.IsNullOrEmpty(spec)) return value ?? "";

                double number;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                {
                    try { return number.ToString(spec, CultureInfo.InvariantCulture); }
                    catch (FormatException) { return value; }
                }

                return value ?? "";
            });
        }

        /// <summary>把配置里写的 <c>\r\n</c> / <c>\t</c> 还原成真实字符。</summary>
        public static string Unescape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
        }
    }
}
