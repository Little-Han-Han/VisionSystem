using System;
using System.Collections.Generic;
using System.Drawing;
using VisionPlatform.Flow;
using VisionPlatform.Framework.Modules;

namespace VisionPlatform.Modules
{
    /// <summary>
    /// 内置模块描述：把「身份 + 端子 + 默认参数 + 参数表 + 界面提示」绑成一个对象。
    /// <para>
    /// 本类型不引用任何宿主类型（窗体、执行器、原生算法），只依赖
    /// <c>VisionPlatform.Framework</c> 的契约与数据类型。
    /// </para>
    /// </summary>
    internal sealed class BuiltInModule : IFlowNodeModule
    {
        private readonly FlowNodeType _type;
        private readonly NodeUiHints _ui;

        public BuiltInModule(FlowNodeType type, string key, string displayName,
                             string categoryKey, int order, string description, NodeUiHints ui)
        {
            _type = type;
            Key = key ?? type.ToString();
            DisplayName = displayName ?? Key;
            CategoryKey = categoryKey ?? ModuleCategories.Output;
            Order = order;
            Description = description ?? "";
            _ui = ui ?? new NodeUiHints();
        }

        public string Key { get; private set; }
        public string DisplayName { get; private set; }
        public string CategoryKey { get; private set; }
        public int Order { get; private set; }
        public string Description { get; private set; }

        public List<FlowPortDef> Inputs { get { return BuiltInPorts.Inputs(_type); } }
        public List<FlowPortDef> Outputs { get { return BuiltInPorts.Outputs(_type); } }

        public List<ParamItem> CreateDefaults() { return BuiltInDefaults.Create(_type); }
        public NodeParamTable CreateParams() { return BuiltInParamTables.Create(_type); }
        public NodeUiHints Ui { get { return _ui; } }

        public Dictionary<string, string> CreateNewNodeOverrides()
        {
            return BuiltInDefaults.NewNodeOverrides(_type);
        }
    }

    /// <summary>
    /// 内置模块包：28 个模块的<b>唯一清单</b>（顺序 = 历史工具箱顺序，不得随意调整）。
    /// <para>
    /// 【新增算法怎么做】在本文件的清单里追加一行（Key / 显示名 / 分类 / 色条 / 预览），
    /// 再到 <see cref="BuiltInDefaults"/>（默认参数）、<see cref="BuiltInParamTables"/>（参数表）、
    /// <see cref="BuiltInPorts"/>（端子）各补一段即可 —— 全部在模块包内，宿主界面零改动。
    /// </para>
    /// </summary>
    internal static class BuiltInModules
    {
        public static void RegisterAll()
        {
            int n = 0;

            Add(FlowNodeType.ImageSource, "ImageSource", "图像源",
                ModuleCategories.Acquisition, ++n, "相机 / 图片文件 / 图片文件夹轮播",
                NodeStripeColors.Acquisition);

            Add(FlowNodeType.Preprocess, "Preprocess", "预处理",
                ModuleCategories.ImageProcessing, ++n, "灰度 / 滤波 / 二值化 / 形态学",
                NodeStripeColors.Processing);

            Add(FlowNodeType.PositionCorrect, "PositionCorrect", "位置修正",
                ModuleCategories.Location, ++n, "用基准位姿修正后续 ROI",
                NodeStripeColors.Location);

            Add(FlowNodeType.ShapeMatch, "ShapeMatch", "形状匹配",
                ModuleCategories.Location, ++n, "基于边缘的形状模板匹配（支持旋转 / 缩放）",
                NodeStripeColors.Location, TemplateHints("ShapeMatch"));

            Add(FlowNodeType.TemplateMatch, "TemplateMatch", "模板匹配(NCC)",
                ModuleCategories.Location, ++n, "归一化灰度相关匹配（仅平移）",
                NodeStripeColors.Location, TemplateHints("TemplateMatch"));

            Add(FlowNodeType.ObjectDetect, "ObjectDetect", "目标识别(YOLO)",
                ModuleCategories.DeepLearning, ++n, "OpenVINO YOLO 目标检测",
                NodeStripeColors.Recognition);

            Add(FlowNodeType.Ocr, "Ocr", "字符识别(OCR)",
                ModuleCategories.Recognition, ++n, "字符分割 + 模板匹配（一字符一模板）",
                NodeStripeColors.Recognition, OcrHints());

            Add(FlowNodeType.BarcodeRead, "BarcodeRead", "条码/二维码",
                ModuleCategories.Recognition, ++n, "ZXing 识别（多码 / 免模板）",
                NodeStripeColors.Recognition);

            Add(FlowNodeType.Blob, "Blob", "Blob 分析",
                ModuleCategories.Segmentation, ++n, "连通域统计与筛选",
                NodeStripeColors.Recognition);

            Add(FlowNodeType.LineCaliper, "LineCaliper", "直线卡尺",
                ModuleCategories.Caliper, ++n, "投影找边 + 直线拟合 + 直线度",
                NodeStripeColors.Measurement,
                PreviewHints(new LayoutPreviewSpec(LayoutPreviewKind.LineCaliper, "",
                    "卡尺 {0} 个 · 投影 {1:0.#}px · 宽度 {2:0.#}px（未运行：示意布局）")));

            Add(FlowNodeType.CircleCaliper, "CircleCaliper", "圆卡尺",
                ModuleCategories.Caliper, ++n, "径向投影找边 + 圆拟合 + 圆度",
                NodeStripeColors.Measurement,
                PreviewHints(new LayoutPreviewSpec(LayoutPreviewKind.CircleCaliper, "",
                    "环形卡尺 {0} 个 · 投影 {1:0.#}px · 宽度 {2:0.#}px（未运行：示意布局）")));

            Add(FlowNodeType.LineIntersect, "LineIntersect", "交点卡尺",
                ModuleCategories.Caliper, ++n, "两组扫描线各找一条线，求延长线交点",
                NodeStripeColors.Measurement,
                PreviewHints(
                    new LayoutPreviewSpec(LayoutPreviewKind.LineCaliper, "Line1", ""),
                    new LayoutPreviewSpec(LayoutPreviewKind.LineCaliper, "Line2",
                        "交点卡尺：两组扫描线（已接「直线1 / 直线2」端子的一侧不用这里的参数）")));

            Add(FlowNodeType.EdgePair, "EdgePair", "边缘对查找",
                ModuleCategories.Caliper, ++n, "沿法向一维取样，按极性配对测线宽",
                NodeStripeColors.Measurement,
                PreviewHints(new LayoutPreviewSpec(LayoutPreviewKind.LineCaliper, "",
                    "边缘对：卡尺 {0} 个 · 投影 {1:0.#}px（投影长度要大于被测线宽）")));

            Add(FlowNodeType.PointPointMeasure, "PointPointMeasure", "点点测量",
                ModuleCategories.Caliper, ++n, "两点距离 / 方向角 / 中点",
                NodeStripeColors.Measurement);

            Add(FlowNodeType.PointLineMeasure, "PointLineMeasure", "点直线测量",
                ModuleCategories.Caliper, ++n, "点到直线垂距 / 垂足",
                NodeStripeColors.Measurement);

            Add(FlowNodeType.LineLineMeasure, "LineLineMeasure", "线线测量",
                ModuleCategories.Caliper, ++n, "两直线夹角 / 交点 / 平行间距",
                NodeStripeColors.Measurement);

            Add(FlowNodeType.Sharpness, "Sharpness", "清晰度",
                ModuleCategories.ImageProcessing, ++n, "清晰度评分（对焦判定）",
                NodeStripeColors.Measurement);

            Add(FlowNodeType.FitCircle, "FitCircle", "圆拟合",
                ModuleCategories.Segmentation, ++n, "由轮廓点拟合圆",
                NodeStripeColors.Measurement);

            Add(FlowNodeType.FitLine, "FitLine", "直线拟合",
                ModuleCategories.Segmentation, ++n, "由轮廓点拟合直线",
                NodeStripeColors.Measurement);

            Add(FlowNodeType.ResultOutput, "ResultOutput", "结果输出",
                ModuleCategories.Output, ++n, "流程结果汇总",
                NodeStripeColors.Output);

            Add(FlowNodeType.SendData, "SendData", "发送数据",
                ModuleCategories.Output, ++n, "按名称绑定通讯并发送",
                NodeStripeColors.Output);

            Add(FlowNodeType.Script, "Script", "脚本",
                ModuleCategories.Output, ++n, "C# 脚本读取上游输出并计算",
                NodeStripeColors.Script);

            Add(FlowNodeType.DlSeg, "DlSeg", "目标语义分割",
                ModuleCategories.DeepLearning, ++n, "YOLO 语义分割",
                NodeStripeColors.Recognition);

            Add(FlowNodeType.DlPose, "DlPose", "目标姿态估计",
                ModuleCategories.DeepLearning, ++n, "YOLO 姿态估计",
                NodeStripeColors.Recognition);

            Add(FlowNodeType.DlDepth, "DlDepth", "目标深度估计",
                ModuleCategories.DeepLearning, ++n, "单目深度估计",
                NodeStripeColors.Recognition);

            Add(FlowNodeType.OcrDl, "OcrDl", "OCR(深度学习)",
                ModuleCategories.DeepLearning, ++n, "PaddleOCR（DB 检测 + CRNN 识别）",
                NodeStripeColors.Recognition);

            Add(FlowNodeType.ReceiveData, "ReceiveData", "接收数据",
                ModuleCategories.Output, ++n, "按名称绑定通讯并接收",
                NodeStripeColors.Output);

            Add(FlowNodeType.CalibTransform, "CalibTransform", "标定转换",
                ModuleCategories.Location, ++n, "像素坐标 → 机械坐标",
                NodeStripeColors.Location);

            Add(FlowNodeType.DistortionCorrect, "DistortionCorrect", "畸变矫正",
                ModuleCategories.ImageProcessing, ++n, "按相机内参去畸变",
                NodeStripeColors.Processing);

            // ---- 3D 视觉 ----
            Add(FlowNodeType.PointCloudSource, "PointCloudSource", "读取点云",
                ModuleCategories.ThreeD, ++n, "PLY/PCD 文件或深度图 → 3D 点云（视图自动进 3D 模式，可旋转）",
                NodeStripeColors.ThreeD);
        }

        private static void Add(FlowNodeType type, string key, string name,
                                string category, int order, string description,
                                Color stripe, NodeUiHints hints = null)
        {
            if (hints == null) hints = new NodeUiHints();
            hints.StripeColor = stripe;
            FlowModuleRegistry.Register(new BuiltInModule(type, key, name, category, order, description, hints));
        }

        private static NodeUiHints TemplateHints(string algorithm)
        {
            var h = new NodeUiHints();
            h.BannerKind = "template";
            h.TemplateAlgorithm = algorithm;
            return h;
        }

        private static NodeUiHints OcrHints()
        {
            var h = new NodeUiHints();
            h.BannerKind = "ocr";
            return h;
        }

        private static NodeUiHints PreviewHints(params LayoutPreviewSpec[] previews)
        {
            var h = new NodeUiHints();
            if (previews != null)
            {
                foreach (LayoutPreviewSpec p in previews)
                    if (p != null && p.Kind != LayoutPreviewKind.None) h.Previews.Add(p);
            }
            return h;
        }
    }

    /// <summary>模块包入口（宿主通过反射创建本类型）。</summary>
    public sealed class BuiltInModulesPackage : IFlowModulePackage
    {
        public string Name { get { return "VisionPlatform 内置模块"; } }

        public void Register()
        {
            BuiltInModules.RegisterAll();
        }
    }
}
