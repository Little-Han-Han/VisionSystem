using System;
using System.Collections.Generic;
using VisionPlatform.Native;

namespace VisionPlatform.Vision
{
    /// <summary>一次检测的结果。</summary>
    public class InspectionResult
    {
        public DateTime Time { get; set; }

        /// <summary>总判定：OK / NG。</summary>
        public bool Passed { get; set; }

        /// <summary>一句话结论。</summary>
        public string Summary { get; set; }

        /// <summary>处理耗时（毫秒）。</summary>
        public long ElapsedMs { get; set; }

        /// <summary>各检测项的明细信息。</summary>
        public List<string> Messages { get; set; }

        /// <summary>预处理后的灰度图（用于在界面上显示中间结果）。</summary>
        public System.Drawing.Bitmap GrayImage { get; set; }

        public double Sharpness { get; set; }
        public List<BlobResult> Blobs { get; set; }
        public MatchResult Match { get; set; }
        public CircleResult Circle { get; set; }

        public InspectionResult()
        {
            Time = DateTime.Now;
            Messages = new List<string>();
            Blobs = new List<BlobResult>();
            Summary = string.Empty;
        }

        public override string ToString()
        {
            return string.Format("{0:HH:mm:ss}  {1}  {2}  ({3} ms)",
                Time, Passed ? "OK" : "NG", Summary, ElapsedMs);
        }
    }
}
