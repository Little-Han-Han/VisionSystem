using System;
using System.Collections.Generic;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 算法模块目录项：工具箱拖拽源与节点创建的<strong>唯一数据源</strong>。
    /// Key 为稳定标识（也是 <see cref="FlowNode.Type"/> 存的值），DisplayName 为界面显示名。
    /// </summary>
    public sealed class FlowModuleInfo
    {
        /// <summary>稳定标识（等于对应的 <see cref="FlowNodeType"/> 枚举名）。</summary>
        public string Key { get; private set; }

        /// <summary>界面显示名（工具箱纯文字块）。</summary>
        public string DisplayName { get; private set; }

        /// <summary>节点类型。</summary>
        public FlowNodeType Type { get; private set; }

        /// <summary>
        /// 所属分类 Key（对应 <see cref="FlowModule.Categories"/> 里的 <see cref="FlowModuleCategory.Key"/>）。
        /// <para>只用于<strong>展示时分组</strong>（工具箱树），不改变 <see cref="FlowModule.All"/> 的既有顺序。</para>
        /// </summary>
        public string Category { get; private set; }

        // ---------------------------------------------------------------------
        //  端子表转发：让工具箱 / 画布 / 校验器 / 执行器共用同一个真源
        //  （FlowPortSchema），任何一处都不许另外硬编码端子名。
        // ---------------------------------------------------------------------

        /// <summary>该模块的输入端子列表（顺序即画布自上而下的绘制顺序）；未知类型返回空列表。</summary>
        public List<FlowPortDef> Inputs
        {
            get { return FlowPortSchema.InputsOf(Type); }
        }

        /// <summary>该模块的输出端子列表（第 1 项为「主输出」）；未知类型返回空列表。</summary>
        public List<FlowPortDef> Outputs
        {
            get { return FlowPortSchema.OutputsOf(Type); }
        }

        /// <summary>主输入端子 Key（第 1 个必填输入）；无输入端子时为 <c>""</c>。</summary>
        public string PrimaryInput
        {
            get { return FlowPortSchema.PrimaryInput(Type); }
        }

        /// <summary>主输出端子 Key（第 1 个输出）；无输出端子时为 <c>""</c>。</summary>
        public string PrimaryOutput
        {
            get { return FlowPortSchema.PrimaryOutput(Type); }
        }

        /// <summary>
        /// 构造一个模块目录项。
        /// <para>
        /// ⚠️ <paramref name="category"/> 是第 4 个<strong>没有默认值</strong>的参数：
        /// 故意不给默认值，是为了让编译器强制把全部 12 个调用点都补上分类 ——
        /// 一旦给默认值，漏改的模块会在工具箱里<strong>静默消失</strong>（分组时匹配不到分类），
        /// 且只有肉眼能发现，冒烟也抓不到。
        /// </para>
        /// </summary>
        /// <param name="key">稳定标识（等于 <see cref="FlowNodeType"/> 枚举名）。</param>
        /// <param name="displayName">界面显示名。</param>
        /// <param name="type">节点类型。</param>
        /// <param name="category">分类 Key，取自 <see cref="FlowModule"/> 的 Cat* 常量。</param>
        public FlowModuleInfo(string key, string displayName, FlowNodeType type, string category)
        {
            Key = key;
            DisplayName = displayName;
            Type = type;
            Category = category;
        }

        public override string ToString() { return DisplayName; }

        /// <summary>
        /// 生成该类型节点的默认参数列表。
        /// 默认值口径见 PRD §4.2 各条与 §7 默认假设 B（位置修正）/ C（卡尺）。
        /// </summary>
        public List<ParamItem> CreateDefaultParams()
        {
            var list = new List<ParamItem>();

            switch (Type)
            {
                case FlowNodeType.ImageSource:
                    // 数据源：Camera = 流程绑定相机；File = 本地图片文件（无相机也能跑通整链）
                    Add(list, "Source", "Camera");
                    Add(list, "FilePath", "");
                    Add(list, "TimeoutMs", "3000");
                    // ROI 裁剪：宽或高为 0 = 全图
                    Add(list, "RoiX", "0");
                    Add(list, "RoiY", "0");
                    Add(list, "RoiW", "0");
                    Add(list, "RoiH", "0");
                    // 旋转 0 / 90 / 180 / 270，水平镜像 0/1
                    Add(list, "Rotate", "0");
                    Add(list, "Mirror", "0");
                    break;

                case FlowNodeType.Preprocess:
                    Add(list, "ToGray", "1");
                    Add(list, "BlurKsize", "3");
                    Add(list, "Threshold", "128");
                    Add(list, "Invert", "0");
                    Add(list, "MorphOp", "0");
                    Add(list, "MorphKsize", "3");
                    break;

                case FlowNodeType.PositionCorrect:
                    // 基准来源：Auto = 取上游最近匹配结果的第 1 个目标；Manual = 手动示教位姿
                    Add(list, "RefSource", "Auto");
                    Add(list, "RefX", "0");
                    Add(list, "RefY", "0");
                    Add(list, "RefAngle", "0");
                    Add(list, "EnableAngle", "1");     // 0 = 只平移不旋转
                    // 超差上限：0 = 不限
                    Add(list, "MaxShiftX", "0");
                    Add(list, "MaxShiftY", "0");
                    Add(list, "MaxAngle", "0");
                    Add(list, "MaxScaleDev", "0");
                    break;

                case FlowNodeType.ShapeMatch:
                    // 模板 ROI（宽高 0 = 整图）
                    Add(list, "TemplateX", "0");
                    Add(list, "TemplateY", "0");
                    Add(list, "TemplateW", "0");
                    Add(list, "TemplateH", "0");
                    // 训练参数
                    Add(list, "Contrast", "30");
                    Add(list, "MaxFeatures", "1500");
                    Add(list, "MinFeatureDist", "2");
                    // 搜索参数
                    Add(list, "SearchX", "0");
                    Add(list, "SearchY", "0");
                    Add(list, "SearchW", "0");
                    Add(list, "SearchH", "0");
                    Add(list, "AngleStart", "-10");
                    Add(list, "AngleEnd", "10");
                    Add(list, "AngleStep", "1");
                    Add(list, "ScaleMin", "1");
                    Add(list, "ScaleMax", "1");
                    Add(list, "ScaleStep", "0");
                    Add(list, "PyramidLevels", "4");
                    Add(list, "MinScore", "0.7");
                    Add(list, "MaxMatches", "1");
                    Add(list, "MaxOverlap", "0.5");
                    Add(list, "Subpixel", "1");
                    Add(list, "Greediness", "0.9");
                    break;

                case FlowNodeType.TemplateMatch:
                    // NCC（归一化灰度相关）：仅支持平移，不支持旋转 / 缩放
                    Add(list, "TemplateX", "0");
                    Add(list, "TemplateY", "0");
                    Add(list, "TemplateW", "0");
                    Add(list, "TemplateH", "0");
                    Add(list, "SearchX", "0");
                    Add(list, "SearchY", "0");
                    Add(list, "SearchW", "0");
                    Add(list, "SearchH", "0");
                    Add(list, "MinScore", "0.8");
                    Add(list, "MaxMatches", "1");
                    Add(list, "Subpixel", "1");
                    break;

                case FlowNodeType.Blob:
                    // 二值化方式：Inherit = 继承上游处理结果；Otsu = 自动阈值；Manual = 手动阈值
                    Add(list, "BinMode", "Inherit");
                    Add(list, "Threshold", "128");
                    // 极性：Bright = 亮底上的暗目标取反前统计白；Dark = 统计暗目标
                    Add(list, "Polarity", "Bright");
                    Add(list, "MinArea", "100");
                    Add(list, "MaxArea", "0");     // 0 = 不限
                    Add(list, "MinWidth", "0");
                    Add(list, "MinHeight", "0");
                    Add(list, "MinCircularity", "0");   // 0 = 不筛（填充度口径，见执行器注释）
                    Add(list, "MaxCount", "50");
                    Add(list, "SortBy", "Area");
                    // 判定：期望个数范围 + 单个面积范围（0 = 不限）
                    Add(list, "ExpectMin", "0");
                    Add(list, "ExpectMax", "0");
                    Add(list, "AreaMin", "0");
                    Add(list, "AreaMax", "0");
                    break;

                case FlowNodeType.LineCaliper:
                    // 扫描线段（ROI，图像坐标）
                    Add(list, "X1", "100");
                    Add(list, "Y1", "100");
                    Add(list, "X2", "300");
                    Add(list, "Y2", "100");
                    Add(list, "CaliperCount", "10");
                    Add(list, "ProjectionLength", "20");
                    Add(list, "Polarity", "0");          // 0=Any 1=DarkToLight 2=LightToDark
                    Add(list, "EdgeThreshold", "30");
                    Add(list, "FilterHalfWidth", "2");
                    Add(list, "SelectMode", "0");        // 0=Strongest 1=First 2=Last
                    Add(list, "MinEdgeCount", "3");
                    Add(list, "RejectOutliers", "1");
                    Add(list, "OutlierTolerance", "2");
                    // 判定（0 = 不限）
                    Add(list, "LengthMin", "0");
                    Add(list, "LengthMax", "0");
                    Add(list, "AngleMin", "0");
                    Add(list, "AngleMax", "0");
                    Add(list, "MaxRms", "0");
                    break;

                case FlowNodeType.CircleCaliper:
                    // 环形 ROI（图像坐标）
                    Add(list, "Cx", "320");
                    Add(list, "Cy", "240");
                    Add(list, "InnerRadius", "80");
                    Add(list, "OuterRadius", "120");
                    Add(list, "StartAngle", "0");
                    Add(list, "AngleSpan", "360");
                    Add(list, "CaliperCount", "20");
                    Add(list, "ProjectionLength", "20");
                    Add(list, "Polarity", "0");
                    Add(list, "EdgeThreshold", "30");
                    Add(list, "FilterHalfWidth", "2");
                    Add(list, "SelectMode", "0");
                    Add(list, "MinEdgeCount", "5");
                    Add(list, "RejectOutliers", "1");
                    Add(list, "OutlierTolerance", "2");
                    // 判定（0 = 不限）
                    Add(list, "DiameterMin", "0");
                    Add(list, "DiameterMax", "0");
                    Add(list, "RoundnessMax", "0");
                    Add(list, "CenterToleranceX", "0");
                    Add(list, "CenterToleranceY", "0");
                    break;

                case FlowNodeType.Sharpness:
                    Add(list, "MinSharpness", "100");
                    break;

                case FlowNodeType.FitCircle:
                case FlowNodeType.FitLine:
                    // 最小轮廓面积过滤（0 = 不限；按拟合结果的等效面积做下限判定）
                    Add(list, "MinContourArea", "0");
                    // 是否在预览上绘制拟合结果
                    Add(list, "DrawShape", "1");
                    break;

                case FlowNodeType.ResultOutput:
                    break;
            }

            return list;
        }

        private static void Add(List<ParamItem> list, string key, string value)
        {
            list.Add(new ParamItem(key, value));
        }
    }

    /// <summary>
    /// 模块分类：工具箱树的<strong>分组节点</strong>（§11.2）。
    /// 一个分类 = 树的一个根节点，其 <see cref="Modules"/> = 该根节点下的叶子。
    /// <para>顺序刻意与数据流方向一致（采集 → 处理 → 定位 → 测量 → 拟合 → 输出），
    /// 这样工具箱从上往下的顺序 ≈ 画布从左往右的连线顺序。</para>
    /// </summary>
    public sealed class FlowModuleCategory
    {
        /// <summary>分类 Key（稳定标识，如 <c>"Location"</c>）。</summary>
        public string Key { get; private set; }

        /// <summary>分类显示名（如「定位与匹配」）。</summary>
        public string DisplayName { get; private set; }

        /// <summary>工具箱中的显示顺序（1..6，越小越靠上）。</summary>
        public int Order { get; private set; }

        /// <summary>该分类下的模块（顺序沿用 <see cref="FlowModule.All"/> 中的相对顺序）。</summary>
        public List<FlowModuleInfo> Modules { get; private set; }

        public FlowModuleCategory(string key, string displayName, int order, List<FlowModuleInfo> modules)
        {
            Key = key;
            DisplayName = displayName;
            Order = order;
            Modules = modules;
        }

        public override string ToString() { return DisplayName; }
    }

    /// <summary>
    /// 模块目录（工具箱与节点创建的唯一数据源）。顺序即工具箱显示顺序（PRD §5.3）。
    /// </summary>
    public static class FlowModule
    {
        // ---------------------------------------------------------------------
        //  分类 Key 常量（§11.2）
        //  用 const 而非 static readonly：编译期常量，不存在静态字段初始化顺序问题，
        //  可以安全地出现在下面的 All 初始化器里。
        // ---------------------------------------------------------------------

        /// <summary>采集（图像源）。</summary>
        public const string CatAcquisition = "Acquisition";

        /// <summary>图像处理（预处理、清晰度）。</summary>
        public const string CatImageProcessing = "ImageProcessing";

        /// <summary>定位与匹配（位置修正、形状匹配、模板匹配）。</summary>
        public const string CatLocation = "Location";

        /// <summary>卡尺与测量（直线卡尺、圆卡尺）。</summary>
        public const string CatCaliper = "Caliper";

        /// <summary>分割与拟合（Blob 分析、圆拟合、直线拟合）。</summary>
        public const string CatSegmentation = "Segmentation";

        /// <summary>输出与通信（结果输出）。</summary>
        public const string CatOutput = "Output";

        /// <summary>全部算法模块，按工具箱显示顺序排列（12 项）。</summary>
        public static readonly List<FlowModuleInfo> All = new List<FlowModuleInfo>
        {
            new FlowModuleInfo("ImageSource",      "图像源",          FlowNodeType.ImageSource,      CatAcquisition),
            new FlowModuleInfo("Preprocess",       "预处理",          FlowNodeType.Preprocess,       CatImageProcessing),
            new FlowModuleInfo("PositionCorrect",  "位置修正",        FlowNodeType.PositionCorrect,  CatLocation),
            new FlowModuleInfo("ShapeMatch",       "形状匹配",        FlowNodeType.ShapeMatch,       CatLocation),
            new FlowModuleInfo("TemplateMatch",    "模板匹配(NCC)",   FlowNodeType.TemplateMatch,    CatLocation),
            new FlowModuleInfo("Blob",             "Blob 分析",       FlowNodeType.Blob,             CatSegmentation),
            new FlowModuleInfo("LineCaliper",      "直线卡尺",        FlowNodeType.LineCaliper,      CatCaliper),
            new FlowModuleInfo("CircleCaliper",    "圆卡尺",          FlowNodeType.CircleCaliper,    CatCaliper),
            new FlowModuleInfo("Sharpness",        "清晰度",          FlowNodeType.Sharpness,        CatImageProcessing),
            new FlowModuleInfo("FitCircle",        "圆拟合",          FlowNodeType.FitCircle,        CatSegmentation),
            new FlowModuleInfo("FitLine",          "直线拟合",        FlowNodeType.FitLine,          CatSegmentation),
            new FlowModuleInfo("ResultOutput",     "结果输出",        FlowNodeType.ResultOutput,     CatOutput)
        };

        /// <summary>
        /// 分类目录（6 项，顺序 = 工具箱自上而下顺序；每项带该分类下的模块）。
        /// <para>
        /// 合计 1 + 2 + 3 + 2 + 3 + 1 = 12，与 <see cref="All"/> 一一对应、无遗漏无重复。
        /// 这是 <see cref="All"/> 的<strong>另一种组织视图</strong>，<see cref="All"/> 本身一字未动
        /// （<c>SmokeParallel</c> 的用例依赖 <see cref="All"/> 的既有顺序）。
        /// </para>
        /// <para>返回的是共享实例，调用方<strong>不应修改</strong>其中的集合。</para>
        /// </summary>
        public static readonly List<FlowModuleCategory> Categories = BuildCategories();

        /// <summary>
        /// 分类视图入口（与 <see cref="Categories"/> 等价，取名对齐调用方的习惯写法）。
        /// 返回共享实例，调用方不应修改。
        /// </summary>
        public static List<FlowModuleCategory> GroupByCategory()
        {
            return Categories;
        }

        /// <summary>按节点类型取所属分类；未知类型返回 null。</summary>
        public static FlowModuleCategory CategoryOf(FlowNodeType type)
        {
            FlowModuleInfo m = FindByType(type);
            if (m == null) return null;
            return CategoryOfKey(m.Category);
        }

        /// <summary>按分类 Key 取分类；未知 Key 返回 null。</summary>
        public static FlowModuleCategory CategoryOfKey(string categoryKey)
        {
            if (string.IsNullOrEmpty(categoryKey)) return null;
            foreach (FlowModuleCategory c in Categories)
            {
                if (string.Equals(c.Key, categoryKey, StringComparison.OrdinalIgnoreCase)) return c;
            }
            return null;
        }

        /// <summary>
        /// 构造分类目录：按固定的 6 个分类顺序建壳，再把 <see cref="All"/> 里的模块
        /// 按 <see cref="FlowModuleInfo.Category"/> 归位（保持 <see cref="All"/> 的相对顺序）。
        /// </summary>
        private static List<FlowModuleCategory> BuildCategories()
        {
            var keys = new[]
            {
                CatAcquisition, CatImageProcessing, CatLocation,
                CatCaliper, CatSegmentation, CatOutput
            };
            var names = new[] { "采集", "图像处理", "定位与匹配", "卡尺与测量", "分割与拟合", "输出与通信" };

            var result = new List<FlowModuleCategory>(keys.Length);
            // 本地索引：Categories 此刻尚未赋值完成（正在初始化），不能走 CategoryOfKey
            var byKey = new Dictionary<string, FlowModuleCategory>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < keys.Length; i++)
            {
                var cat = new FlowModuleCategory(keys[i], names[i], i + 1, new List<FlowModuleInfo>());
                result.Add(cat);
                byKey[cat.Key] = cat;
            }

            // 归位：外层遍历 All，保证同一分类内沿用 All 的既有顺序
            foreach (FlowModuleInfo m in All)
            {
                FlowModuleCategory cat;
                if (m.Category != null && byKey.TryGetValue(m.Category, out cat))
                    cat.Modules.Add(m);
            }

            return result;
        }

        /// <summary>按 Key 查找模块；不存在返回 null。</summary>
        public static FlowModuleInfo FindByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (FlowModuleInfo m in All)
            {
                if (string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }

        /// <summary>按类型查找模块；不存在返回 null。</summary>
        public static FlowModuleInfo FindByType(FlowNodeType type)
        {
            foreach (FlowModuleInfo m in All)
            {
                if (m.Type == type) return m;
            }
            return null;
        }

        /// <summary>按类型创建节点（铺默认参数）。</summary>
        public static FlowNode CreateNode(FlowNodeType type, float x, float y)
        {
            FlowModuleInfo m = FindByType(type);
            var node = new FlowNode
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = type.ToString(),
                Name = (m != null) ? m.DisplayName : type.ToString(),
                X = x,
                Y = y,
                Params = (m != null) ? m.CreateDefaultParams() : new List<ParamItem>()
            };
            return node;
        }

        /// <summary>按模块 Key 创建节点（工具箱拖入时使用）；未识别 Key 返回 null。</summary>
        public static FlowNode CreateNodeByKey(string key, float x, float y)
        {
            FlowModuleInfo m = FindByKey(key);
            return (m != null) ? CreateNode(m.Type, x, y) : null;
        }

        // =====================================================================
        //  产品聚合与过滤（FR-MP-02 / FR-MP-04）
        // =====================================================================

        /// <summary>
        /// 收集流程列表中出现过的产品型号：去重 + 按 <see cref="StringComparison.Ordinal"/> 排序。
        /// <c>""</c>（通用 / 未分类）<strong>不进入</strong>列表——由调用方用「全部」与「通用」区分。
        /// </summary>
        /// <param name="flows">流程列表；为 null 时返回空列表。</param>
        public static List<string> CollectProducts(List<FlowDefinition> flows)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (flows == null) return result;

            foreach (FlowDefinition f in flows)
            {
                if (f == null) continue;

                string p = f.Product ?? "";
                if (p.Length == 0) continue;          // 通用流程不算一个产品
                if (seen.Add(p)) result.Add(p);
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>
        /// 按产品过滤流程：<paramref name="product"/> 为 null 或 "" 时原样返回全部。
        /// 只影响显示与「全部启动」的作用域，<strong>绝不删除数据、绝不改写流程的 Product</strong>。
        /// </summary>
        /// <param name="flows">流程列表；为 null 时返回空列表。</param>
        /// <param name="product">产品型号；"" = 全部。</param>
        public static List<FlowDefinition> FilterByProduct(List<FlowDefinition> flows, string product)
        {
            var result = new List<FlowDefinition>();
            if (flows == null) return result;

            if (string.IsNullOrEmpty(product))
            {
                foreach (FlowDefinition f in flows) if (f != null) result.Add(f);
                return result;
            }

            foreach (FlowDefinition f in flows)
            {
                if (f == null) continue;

                // 通用流程（Product=""）在任何产品筛选下都显示
                string p = f.Product ?? "";
                if (p.Length == 0 || string.Equals(p, product, StringComparison.Ordinal))
                    result.Add(f);
            }

            return result;
        }
    }
}
