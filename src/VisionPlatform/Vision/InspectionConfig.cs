using System;
using VisionPlatform.Native;

namespace VisionPlatform.Vision
{
    /// <summary>
    /// 检测参数（配方的一部分）。界面上的算法面板直接编辑本对象。
    /// </summary>
    public class InspectionConfig
    {
        // ---------- 预处理 ----------
        public PreprocessParam Preprocess { get; set; }

        // ---------- 各检测项开关 ----------
        public bool EnableSharpness { get; set; }
        public bool EnableBlob { get; set; }
        public bool EnableTemplate { get; set; }
        public bool EnableCircleFit { get; set; }

        // ---------- 判定阈值 ----------
        /// <summary>清晰度下限，低于此值判 NG。</summary>
        public double MinSharpness { get; set; }

        /// <summary>Blob 面积下限。</summary>
        public double MinBlobArea { get; set; }

        /// <summary>Blob 面积上限（0 表示不限）。</summary>
        public double MaxBlobArea { get; set; }

        /// <summary>期望的 Blob 数量（0 表示不校验数量）。</summary>
        public int ExpectedBlobCount { get; set; }

        /// <summary>模板匹配得分下限。</summary>
        public double MinMatchScore { get; set; }

        /// <summary>圆拟合半径的标称值与公差。</summary>
        public double NominalRadius { get; set; }
        public double RadiusTolerance { get; set; }

        public InspectionConfig()
        {
            Preprocess = PreprocessParam.Default();
            EnableSharpness = false;
            EnableBlob = true;
            EnableTemplate = false;
            EnableCircleFit = false;
            MinSharpness = 50;
            MinBlobArea = 100;
            MaxBlobArea = 0;
            ExpectedBlobCount = 0;
            MinMatchScore = 0.8;
            NominalRadius = 0;
            RadiusTolerance = 5;
        }

        public InspectionConfig Clone()
        {
            return (InspectionConfig)MemberwiseClone();
        }
    }
}
