using System;
using System.Collections.Generic;
using VisionPlatform.Flow;
using VisionPlatform.Framework.Modules;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 算法模块目录项：工具箱拖拽源与节点创建的唯一数据源。
    /// <para>
    /// 【重构后】本类型只是 <see cref="IFlowNodeModule"/> 的宿主侧视图：
    /// 真正的清单 / 端子 / 默认参数 / 参数表都在模块包里，本类型不再包含任何按类型的 switch。
    /// </para>
    /// </summary>
    public sealed class FlowModuleInfo
    {
        private readonly IFlowNodeModule _module;

        /// <summary>稳定标识（等于对应的 <see cref="FlowNodeType"/> 枚举名）。</summary>
        public string Key { get; private set; }

        /// <summary>界面显示名（工具箱纯文字块）。</summary>
        public string DisplayName { get; private set; }

        /// <summary>节点类型。</summary>
        public FlowNodeType Type { get; private set; }

        /// <summary>所属分类 Key（对应 <see cref="FlowModule.Categories"/> 里的 Key）。</summary>
        public string Category { get; private set; }

        /// <summary>模块描述（工具提示用）。</summary>
        public string Description { get { return _module != null ? _module.Description : ""; } }

        /// <summary>模块包里的原始描述对象（界面可读它的 Ui 提示；为 null 表示无模块）。</summary>
        public IFlowNodeModule Module { get { return _module; } }

        internal FlowModuleInfo(IFlowNodeModule module)
        {
            _module = module;
            Key = module.Key;

            FlowNodeType type;
            Type = Enum.TryParse(module.Key, out type) ? type : FlowNodeType.ResultOutput;

            DisplayName = module.DisplayName;
            Category = module.CategoryKey;
        }

        /// <summary>该模块的输入端子列表；未知类型返回空列表。</summary>
        public List<FlowPortDef> Inputs
        {
            get { return FlowPortSchema.InputsOf(Type); }
        }

        /// <summary>该模块的输出端子列表（第 1 项为主输出）；未知类型返回空列表。</summary>
        public List<FlowPortDef> Outputs
        {
            get { return FlowPortSchema.OutputsOf(Type); }
        }

        /// <summary>主输入端子 Key；无输入端子时为 <c>""</c>。</summary>
        public string PrimaryInput { get { return FlowPortSchema.PrimaryInput(Type); } }

        /// <summary>主输出端子 Key；无输出端子时为 <c>""</c>。</summary>
        public string PrimaryOutput { get { return FlowPortSchema.PrimaryOutput(Type); } }

        public override string ToString() { return DisplayName; }

        /// <summary>
        /// 新建节点时用的默认参数：先取 <see cref="CreateDefaultParams"/>，再应用模块的"只该给新节点"覆盖值。
        /// <para>
        /// 【为什么必须分成两个入口】<see cref="CreateDefaultParams"/> 同时被"新建节点"和
        /// "老配置升级补键"（<c>FlowSettings.UpgradeToCurrent</c>）两处复用。
        /// 直接改它的 <c>TemplateDir</c> 默认值，会让更老的、还没有这个键的配置在升级时
        /// 指向一个不存在的新目录 → 用户原有模板全部"消失"。
        /// </para>
        /// </summary>
        public Dictionary<string, string> CreateNewParams()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ParamItem p in CreateDefaultParams())
                if (p != null && p.Key != null) map[p.Key] = p.Value;

            if (_module != null)
            {
                foreach (KeyValuePair<string, string> kv in _module.CreateNewNodeOverrides())
                    map[kv.Key] = kv.Value;
            }

            return map;
        }

        /// <summary>生成该类型节点的默认参数列表（来自模块包）。</summary>
        public List<ParamItem> CreateDefaultParams()
        {
            return (_module != null) ? _module.CreateDefaults() : new List<ParamItem>();
        }
    }

    /// <summary>
    /// 模块分类：工具箱树的分组节点。
    /// 一个分类 = 树的一个根节点，其 <see cref="Modules"/> = 该根节点下的叶子。
    /// </summary>
    public sealed class FlowModuleCategory
    {
        /// <summary>分类 Key（稳定标识，如 <c>"Location"</c>）。</summary>
        public string Key { get; private set; }

        /// <summary>分类显示名（如「定位与匹配」）。</summary>
        public string DisplayName { get; private set; }

        /// <summary>工具箱中的显示顺序（越小越靠上）。</summary>
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
    /// 模块目录门面（工具箱与节点创建的唯一数据源）。
    /// <para>
    /// 数据来自 <see cref="FlowModuleRegistry"/>（模块包动态加载）；本类只做类型适配与排序，
    /// <b>新增算法不再需要修改本文件</b>。
    /// </para>
    /// </summary>
    public static class FlowModule
    {
        /// <summary>采集（图像源）。</summary>
        public const string CatAcquisition = ModuleCategories.Acquisition;

        /// <summary>图像处理（预处理、清晰度）。</summary>
        public const string CatImageProcessing = ModuleCategories.ImageProcessing;

        /// <summary>定位与匹配（位置修正、形状匹配、模板匹配）。</summary>
        public const string CatLocation = ModuleCategories.Location;

        /// <summary>卡尺与测量（直线卡尺、圆卡尺）。</summary>
        public const string CatCaliper = ModuleCategories.Caliper;

        /// <summary>分割与拟合（Blob 分析、圆拟合、直线拟合）。</summary>
        public const string CatSegmentation = ModuleCategories.Segmentation;

        /// <summary>输出与通信（结果输出）。</summary>
        public const string CatOutput = ModuleCategories.Output;

        /// <summary>识别（字符识别 OCR）。</summary>
        public const string CatRecognition = ModuleCategories.Recognition;

        /// <summary>深度学习（OpenVINO 推理）。</summary>
        public const string CatDeepLearning = ModuleCategories.DeepLearning;

        /// <summary>全部算法模块，按工具箱显示顺序排列（来自模块包，历史顺序保持不变）。</summary>
        public static readonly List<FlowModuleInfo> All = BuildAll();

        /// <summary>分类目录（顺序 = 工具箱自上而下顺序；每项带该分类下的模块）。</summary>
        public static readonly List<FlowModuleCategory> Categories = BuildCategories();

        private static List<FlowModuleInfo> BuildAll()
        {
            var list = new List<FlowModuleInfo>();
            foreach (IFlowNodeModule m in FlowModuleRegistry.Modules)
                list.Add(new FlowModuleInfo(m));
            return list;
        }

        /// <summary>分类视图入口（返回共享实例，调用方不应修改）。</summary>
        public static List<FlowModuleCategory> GroupByCategory() { return Categories; }

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

        private static List<FlowModuleCategory> BuildCategories()
        {
            var result = new List<FlowModuleCategory>(ModuleCategories.All.Count);
            var byKey = new Dictionary<string, FlowModuleCategory>(StringComparer.OrdinalIgnoreCase);

            foreach (ModuleCategory c in ModuleCategories.All)
            {
                var cat = new FlowModuleCategory(c.Key, c.DisplayName, c.Order, new List<FlowModuleInfo>());
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
                Params = BuildInitialParams(m, type)
            };
            return node;
        }

        /// <summary>
        /// 新建节点的初始参数：走 <see cref="FlowModuleInfo.CreateNewParams"/> 的新默认值
        /// （目前只有 OCR 的 <c>TemplateDir</c> 不同）。
        /// </summary>
        private static List<ParamItem> BuildInitialParams(FlowModuleInfo m, FlowNodeType type)
        {
            if (m == null) return new List<ParamItem>();

            List<ParamItem> list = m.CreateDefaultParams();

            // 脚本节点：新拖入就带骨架，用户不必从空白开始（编辑器里也是这一份）
            if (type == FlowNodeType.Script)
            {
                bool hasSource = false;
                foreach (ParamItem it in list)
                    if (it != null && it.Key == "Source") hasSource = true;

                if (!hasSource) list.Add(new ParamItem { Key = "Source", Value = ScriptEngine.SampleScript });
                return list;
            }

            if (type != FlowNodeType.Ocr) return list;

            Dictionary<string, string> newDefaults = m.CreateNewParams();
            string dir;
            if (!newDefaults.TryGetValue("TemplateDir", out dir) || string.IsNullOrEmpty(dir)) return list;

            foreach (ParamItem p in list)
            {
                if (p != null && string.Equals(p.Key, "TemplateDir", StringComparison.Ordinal))
                {
                    p.Value = dir;
                    return list;
                }
            }
            list.Add(new ParamItem("TemplateDir", dir));
            return list;
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

        /// <summary>收集流程列表中出现过的产品型号：去重 + 排序（"" 不进入列表）。</summary>
        public static List<string> CollectProducts(List<FlowDefinition> flows)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (flows == null) return result;

            foreach (FlowDefinition f in flows)
            {
                if (f == null) continue;

                string p = f.Product ?? "";
                if (p.Length == 0) continue;
                if (seen.Add(p)) result.Add(p);
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>
        /// 按产品过滤流程：<paramref name="product"/> 为 null 或 "" 时原样返回全部。
        /// 只影响显示与「全部启动」的作用域，绝不删除数据、绝不改写流程的 Product。
        /// </summary>
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

                string p = f.Product ?? "";
                if (p.Length == 0 || string.Equals(p, product, StringComparison.Ordinal))
                    result.Add(f);
            }

            return result;
        }
    }
}
