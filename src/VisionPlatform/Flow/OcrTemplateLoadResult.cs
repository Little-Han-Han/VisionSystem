using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VisionPlatform.Flow
{
    /// <summary>模板库"装完之后的真实状态"。四态互斥，文案由 <see cref="OcrTemplateLoadResult.Brief"/> 唯一生成。</summary>
    public enum OcrTemplateState
    {
        /// <summary>目录里一个模板文件都没有（且目录是空的）。</summary>
        Empty,

        /// <summary>有文件，但一个都没读成功（<strong>不是</strong>"为空"）。</summary>
        AllFailed,

        /// <summary>部分成功、部分失败。</summary>
        Partial,

        /// <summary>全部成功。</summary>
        Ok
    }

    /// <summary>单个模板文件读取失败的原因（文件名 + 可读原因，禁止只给计数）。</summary>
    public sealed class OcrTemplateFailure
    {
        public string FileName = "";
        public string Reason = "";

        public override string ToString()
        {
            return string.IsNullOrEmpty(Reason) ? FileName : FileName + "（" + Reason + "）";
        }
    }

    /// <summary>
    /// OCR 模板加载的<strong>结构化结果</strong>（替代"返回一个 int"）。
    /// <para>
    /// 【为什么要结构化】旧实现只返回数量，于是四件完全不同的事在界面上都表现为"0 个模板"：
    /// 目录为空、全部读取失败、目录不存在、原生句柄异常。前两种必须说成不同的话 ——
    /// 把"读不出来"报成"没有"，用户会去建模板，而真因是那批图本身坏了。
    /// </para>
    /// <para>
    /// 【硬约束】<see cref="OcrTemplateState.AllFailed"/> 时 <see cref="Brief"/> 里<strong>不得出现
    /// "为空 / 0 个 / 目录为空"</strong>字样：那是把一次读失败伪装成一次空目录。
    /// </para>
    /// </summary>
    public sealed class OcrTemplateLoadResult
    {
        /// <summary>实际扫描的绝对目录（已解析相对路径）。</summary>
        public string Dir = "";

        /// <summary>目录下<strong>受支持的图片文件</strong>总数。</summary>
        public int FileCount;

        /// <summary>真正灌进原生上下文的模板数。</summary>
        public int LoadedCount;

        /// <summary>逐个失败清单（文件名 + 原因），不含"只给计数"的情形。</summary>
        public List<OcrTemplateFailure> Failures = new List<OcrTemplateFailure>();

        /// <summary>本次扫描时间。</summary>
        public DateTime ScannedAt = DateTime.MinValue;

        /// <summary>是否直接复用了上一轮的加载结果（指纹未变，未重扫磁盘）。</summary>
        public bool FromCache;

        /// <summary>四态判定。</summary>
        public OcrTemplateState State
        {
            get
            {
                if (FileCount == 0) return OcrTemplateState.Empty;
                if (LoadedCount == 0) return OcrTemplateState.AllFailed;
                return Failures.Count > 0 ? OcrTemplateState.Partial : OcrTemplateState.Ok;
            }
        }

        /// <summary>是否有可用于识别文本的模板（false = 只做字符分割）。</summary>
        public bool HasTextTemplate { get { return LoadedCount > 0; } }

        /// <summary>一句话状态（结果摘要、状态卡片、管理窗体顶部共用）。</summary>
        public string Brief()
        {
            switch (State)
            {
                case OcrTemplateState.Empty:
                    return "字符模板库为空（0 个文件），仅做字符分割、不输出文本";
                case OcrTemplateState.AllFailed:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0} 个文件均读取失败，仅做字符分割、不输出文本", FileCount);
                case OcrTemplateState.Partial:
                    return string.Format(CultureInfo.InvariantCulture,
                        "已加载 {0}/{1} 个字符模板（{2} 个读取失败）", LoadedCount, FileCount, Failures.Count);
                default:
                    return string.Format(CultureInfo.InvariantCulture, "已加载 {0} 个字符模板", LoadedCount);
            }
        }

        /// <summary>
        /// 可行动的提示文案（空库 / 全失败 / 部分失败时非空；全成功时返回空串）。
        /// 措辞必须点明"下一步点什么"，不允许出现"未实现 / 错误码"这类无指引表述。
        /// </summary>
        public string ActionHint()
        {
            switch (State)
            {
                case OcrTemplateState.Empty:
                    return "字符模板库为空，当前仅做字符分割、不输出文本；点『管理模板库…』框选字符入库。";
                case OcrTemplateState.AllFailed:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0} 个文件均读取失败，当前仅做字符分割、不输出文本；请点『管理模板库…』查看失败原因。",
                        FileCount);
                case OcrTemplateState.Partial:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0} 个模板文件读取失败，已用其余 {1} 个正常识别；失败清单见『管理模板库…』底部。",
                        Failures.Count, LoadedCount);
                default:
                    return "";
            }
        }

        /// <summary>失败清单压成一行（最多 <paramref name="max"/> 条，超出用"…"收尾）。</summary>
        public string FailureSummary(int max)
        {
            if (Failures == null || Failures.Count == 0) return "";

            var sb = new StringBuilder();
            int n = Math.Min(Failures.Count, Math.Max(0, max));
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append('、');
                sb.Append(Failures[i].ToString());
            }
            if (Failures.Count > n) sb.Append("…");
            return sb.ToString();
        }
    }
}
