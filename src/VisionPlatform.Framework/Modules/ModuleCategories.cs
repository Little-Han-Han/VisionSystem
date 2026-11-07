using System;
using System.Collections.Generic;
using System.Drawing;

namespace VisionPlatform.Framework.Modules
{
    /// <summary>模块分类（工具箱分组）。顺序 = 工具箱自上而下的顺序。</summary>
    public sealed class ModuleCategory
    {
        public string Key { get; private set; }
        public string DisplayName { get; private set; }
        public int Order { get; private set; }

        public ModuleCategory(string key, string displayName, int order)
        {
            Key = key ?? "";
            DisplayName = displayName ?? "";
            Order = order;
        }

        public override string ToString() { return DisplayName; }
    }

    /// <summary>
    /// 内置分类常量表（唯一真源）。
    /// <para>模块包引用这里的 Key，宿主据此分组；新增分类只需在 <see cref="All"/> 末尾追加。</para>
    /// </summary>
    public static class ModuleCategories
    {
        public const string Acquisition = "Acquisition";
        public const string ImageProcessing = "ImageProcessing";
        public const string Location = "Location";
        public const string Caliper = "Caliper";
        public const string Segmentation = "Segmentation";
        public const string Recognition = "Recognition";
        public const string DeepLearning = "DeepLearning";
        public const string Output = "Output";

        /// <summary>全部分类（顺序即工具箱顺序）。</summary>
        public static readonly List<ModuleCategory> All = new List<ModuleCategory>
        {
            new ModuleCategory(Acquisition,      "采集",          1),
            new ModuleCategory(ImageProcessing,  "图像处理",      2),
            new ModuleCategory(Location,         "定位与匹配",    3),
            new ModuleCategory(Caliper,          "卡尺与测量",    4),
            new ModuleCategory(Segmentation,     "分割与拟合",    5),
            new ModuleCategory(Recognition,      "识别",          6),
            new ModuleCategory(DeepLearning,     "深度学习",      7),
            new ModuleCategory(Output,           "输出与通信",    8)
        };

        /// <summary>按 Key 取分类；未知返回 null。</summary>
        public static ModuleCategory Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (ModuleCategory c in All)
                if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }
    }

    /// <summary>
    /// 节点视觉常量：颜色只依赖 System.Drawing，避免模块包引用宿主主题。
    /// 宿主可以把这些颜色映射到当前主题的近似色。
    /// </summary>
    public static class NodeStripeColors
    {
        public static readonly Color Acquisition = Color.FromArgb(110, 210, 235);
        public static readonly Color Processing = Color.FromArgb(140, 200, 170);
        public static readonly Color Location = Color.FromArgb(190, 150, 245);
        public static readonly Color Recognition = Color.FromArgb(255, 185, 95);
        public static readonly Color Measurement = Color.FromArgb(140, 220, 150);
        public static readonly Color Script = Color.FromArgb(220, 180, 255);
        public static readonly Color Output = Color.FromArgb(160, 170, 190);
    }
}
