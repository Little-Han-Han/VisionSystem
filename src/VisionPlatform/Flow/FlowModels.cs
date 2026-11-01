using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 流程节点类型（仅内存使用；序列化时以字符串形式存入 <see cref="FlowNode.Type"/>）。
    /// </summary>
    /// <remarks>
    /// <strong>只能在枚举末尾追加</strong>：节点类型以「枚举名的字符串」持久化在
    /// appconfig.json 中，改动既有成员的顺序或名字会让旧流程解析失败。
    /// 前 8 项为本期之前的既有类型，后 4 项为本期追加。
    /// 未知类型在运行时按「跳过」处理，不会让整条流程加载失败。
    /// </remarks>
    public enum FlowNodeType
    {
        ImageSource,
        Preprocess,
        ShapeMatch,
        Blob,
        Sharpness,
        FitCircle,
        FitLine,
        ResultOutput,

        // ===== 本期追加（追加在末尾，保证既有序号与字符串不变）=====
        /// <summary>模板匹配（NCC，原生 va_match）：仅支持平移，不支持旋转 / 缩放。</summary>
        TemplateMatch,

        /// <summary>位置修正：输出 dX / dY / dAngle / dScale 并写入执行期 RunTransform。</summary>
        PositionCorrect,

        /// <summary>直线卡尺（原生 va_line_caliper）：投影找边 + 直线拟合 + 直线度 RMS。</summary>
        LineCaliper,

        /// <summary>圆卡尺（原生 va_circle_caliper）：径向投影找边 + 圆拟合 + 圆度 RMS。</summary>
        CircleCaliper,

        // ===== 本期追加（同样加在末尾，保证既有序号与字符串不变）=====

        /// <summary>图像滤波（原生 va_filter）：平滑 / 锐化 + 可选形态学，支持灰度与彩色两种链路。</summary>
        ImageFilter,

        /// <summary>颜色提取（原生 va_color_extract）：BGR / HSV 阈值分割 → 连通域统计。</summary>
        ColorExtractor,

        // ===== 本期追加（追加在末尾，保证既有序号与字符串不变）=====

        /// <summary>目标识别（OpenVINO YOLO，原生 va_det_*）：深度学习目标检测，输出检测框（类别 / 分数 / 坐标）。</summary>
        ObjectDetect,

        // ===== 本期追加（追加在末尾，保证既有序号与字符串不变）=====

        /// <summary>字符识别（传统算法 OCR，原生 va_ocr_*）：字符分割 + 模板匹配，模板为「一个字符一张图、文件名即标签」。</summary>
        Ocr,

        // ===== 本期追加（追加在末尾，保证既有序号与字符串不变）=====

        /// <summary>发送数据：按名称绑定「通讯」界面里已配置的一条通讯，发送固定文本 / 格式化文本 / 写 Modbus 寄存器。</summary>
        SendData,

        /// <summary>接收数据：按名称绑定「通讯」界面里已配置的一条通讯，读取最近一条文本或读 Modbus 保持寄存器。</summary>
        ReceiveData,

        /// <summary>
        /// 脚本节点：用 C# 方法体读取<strong>所有上游节点的输出</strong>（图像尺寸 / 位姿 / 数量 / 分数 / 文本 / 几何数），
        /// 计算结果写回结果区。<c>api</c> 是 <see cref="ScriptApi"/> 实例，<c>return</c> 的字符串即结果摘要。
        /// </summary>
        Script,

        /// <summary>深度学习：目标语义分割（YOLO26-seg，OpenVINO 推理）。输出每个目标的框 + 掩膜面积。</summary>
        DlSeg,

        /// <summary>深度学习：目标姿态估计（YOLO26-pose），输出框 + 关键点。</summary>
        DlPose,

        /// <summary>深度学习：深度估计，输出 0~1 归一化深度图。</summary>
        DlDepth,

        /// <summary>
        /// 深度学习 OCR（PaddleOCR）：DB 文本检测 + CRNN 识别，两阶段。
        /// <para>与传统 OCR（模板匹配，<see cref="Ocr"/>）是两套东西，互不影响。</para>
        /// </summary>
        OcrDl,
}

    /// <summary>
    /// 单个参数项：以键值对列表保存，避免 Dictionary 的 JSON 形态不确定（见架构 3.3）。
    /// </summary>
    [DataContract]
    public sealed class ParamItem
    {
        [DataMember]
        public string Key { get; set; }

        [DataMember]
        public string Value { get; set; }

        public ParamItem() { Key = ""; Value = ""; }

        public ParamItem(string key, string value)
        {
            Key = key;
            Value = value;
        }

        public ParamItem Clone()
        {
            return new ParamItem(Key, Value);
        }
    }

    /// <summary>流程节点：位置用屏幕像素（画布无缩放，画布坐标 = 屏幕坐标）。</summary>
    [DataContract]
    public sealed class FlowNode
    {
        [DataMember]
        public string Id { get; set; }

        /// <summary>节点类型名（<see cref="FlowNodeType"/> 的枚举名，字符串存储保证 JSON 稳定）。</summary>
        [DataMember]
        public string Type { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public float X { get; set; }

        [DataMember]
        public float Y { get; set; }

        [DataMember]
        public List<ParamItem> Params { get; set; }

        public FlowNode()
        {
            Id = "";
            Type = "";
            Name = "";
            Params = new List<ParamItem>();
        }

        /// <summary>解析 <see cref="Type"/> 为枚举；无法识别时返回 false。</summary>
        public bool TryGetType(out FlowNodeType type)
        {
            return Enum.TryParse(Type, out type);
        }

        /// <summary>按 Key 查找参数项；不存在返回 null。</summary>
        public ParamItem FindParam(string key)
        {
            if (Params == null || string.IsNullOrEmpty(key)) return null;
            foreach (ParamItem p in Params)
            {
                if (p != null && string.Equals(p.Key, key, StringComparison.Ordinal)) return p;
            }
            return null;
        }

        /// <summary>写入（或更新）一个字符串参数。</summary>
        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (Params == null) Params = new List<ParamItem>();

            ParamItem p = FindParam(key);
            if (p == null) Params.Add(new ParamItem(key, value));
            else p.Value = value;
        }

        public void Set(string key, double value)
        {
            Set(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public void Set(string key, int value)
        {
            Set(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public void Set(string key, bool value)
        {
            Set(key, value ? "1" : "0");
        }

        public string GetString(string key, string def = "")
        {
            ParamItem p = FindParam(key);
            return (p == null || p.Value == null) ? def : p.Value;
        }

        public double GetDouble(string key, double def = 0.0)
        {
            double v;
            return double.TryParse(GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        public int GetInt(string key, int def = 0)
        {
            int v;
            return int.TryParse(GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        public bool GetBool(string key, bool def = false)
        {
            string s = GetString(key, def ? "1" : "0");
            if (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return def;
        }

        public FlowNode Clone()
        {
            var copy = new FlowNode
            {
                Id = Id,
                Type = Type,
                Name = Name,
                X = X,
                Y = Y,
                Params = new List<ParamItem>()
            };
            if (Params != null)
            {
                foreach (ParamItem p in Params)
                    if (p != null) copy.Params.Add(p.Clone());
            }
            return copy;
        }
    }

    /// <summary>流程连线：从<strong>源节点的某个输出端子</strong>指向<strong>目标节点的某个输入端子</strong>。</summary>
    /// <remarks>
    /// <c>FromPort</c> / <c>ToPort</c> 是 Schema v4 追加的字段（端子化）。
    /// ⚠️ <c>DataContractJsonSerializer</c> <strong>不调用构造函数</strong>，
    /// 旧配置缺失这两个字段时反序列化得到的是 <c>null</c> 而不是 <c>""</c>，
    /// 因此读取处一律先经 <see cref="Normalize"/>（由 <see cref="FlowSettings.UpgradeToCurrent"/> 调用），
    /// 或至少用 <c>?? ""</c> 兜底。
    /// </remarks>
    [DataContract]
    public sealed class FlowLink
    {
        [DataMember]
        public string FromNodeId { get; set; }

        [DataMember]
        public string ToNodeId { get; set; }

        /// <summary>
        /// 源<strong>输出端子</strong> Key（对应 <see cref="FlowPortSchema.OutputsOf"/> 里的某一项）。
        /// 旧配置缺失时为 <c>null</c>，由 <see cref="Normalize"/> 补成主输出。
        /// </summary>
        [DataMember]
        public string FromPort { get; set; }

        /// <summary>
        /// 目标<strong>输入端子</strong> Key（对应 <see cref="FlowPortSchema.InputsOf"/> 里的某一项）。
        /// 旧配置缺失时为 <c>null</c>，由 <see cref="Normalize"/> 补成主输入。
        /// </summary>
        [DataMember]
        public string ToPort { get; set; }

        public FlowLink()
        {
            FromNodeId = "";
            ToNodeId = "";
            FromPort = "";
            ToPort = "";
        }

        /// <summary>
        /// 两参构造（<strong>保留</strong>：既有调用点与冒烟在用）。端子字段留空串，
        /// 后续由 <see cref="Normalize"/> 按两端节点类型补齐。
        /// </summary>
        public FlowLink(string fromNodeId, string toNodeId)
            : this()
        {
            FromNodeId = fromNodeId ?? "";
            ToNodeId = toNodeId ?? "";
        }

        /// <summary>四参构造（端子化后的常规写法）。</summary>
        public FlowLink(string fromNodeId, string fromPort, string toNodeId, string toPort)
            : this()
        {
            FromNodeId = fromNodeId ?? "";
            FromPort = fromPort ?? "";
            ToNodeId = toNodeId ?? "";
            ToPort = toPort ?? "";
        }

        public FlowLink Clone()
        {
            return new FlowLink(FromNodeId, FromPort ?? "", ToNodeId, ToPort ?? "");
        }

        /// <summary>
        /// 归一化一条连线：<c>FromPort</c> / <c>ToPort</c> 为 null、空串、或在该类型端子表中查不到时，
        /// 补成「默认端子」（源取主输出、目标取主输入）。
        /// </summary>
        /// <remarks>
        /// <strong>只补不覆盖 → 幂等</strong>：已是合法值时原样保留并返回 false。
        /// 原则是「缺失即补」而不是「钳制」——钳制会在字段缺失时把功能悄悄改掉
        /// （我们在 <c>MaxParallelFlows</c> 上栽过一次）。
        /// 注意幂等的实现细节：当某类型<strong>没有任何</strong>输出/输入端子时，默认端子是 <c>""</c>，
        /// 此时只在值为 <c>null</c> 时才认为发生了改动，避免每次调用都返回 true。
        /// </remarks>
        /// <param name="flow">所属流程（用于在端子表里查两端节点类型）。</param>
        /// <param name="link">要归一化的连线。</param>
        /// <returns>是否发生了改动。</returns>
        public static bool Normalize(FlowDefinition flow, FlowLink link)
        {
            if (flow == null || link == null) return false;

            FlowNode from = flow.FindNode(link.FromNodeId);
            FlowNode to = flow.FindNode(link.ToNodeId);
            if (from == null || to == null) return false;            // 悬空连线：交给校验器报错

            FlowNodeType fromType, toType;
            if (!from.TryGetType(out fromType) || !to.TryGetType(out toType)) return false;  // 未知类型：原样保留

            bool changed = false;

            string curFrom = link.FromPort ?? "";
            string wantFrom = FlowPortSchema.Find(fromType, FlowPortDir.Out, curFrom) != null
                ? curFrom
                : FlowPortSchema.PrimaryOutput(fromType);

            if (link.FromPort == null || !string.Equals(curFrom, wantFrom, StringComparison.Ordinal))
            {
                link.FromPort = wantFrom;
                changed = true;
            }

            string curTo = link.ToPort ?? "";
            string wantTo = FlowPortSchema.Find(toType, FlowPortDir.In, curTo) != null
                ? curTo
                : FlowPortSchema.PrimaryInput(toType);

            if (link.ToPort == null || !string.Equals(curTo, wantTo, StringComparison.Ordinal))
            {
                link.ToPort = wantTo;
                changed = true;
            }

            return changed;
        }
    }

    /// <summary>一条完整流程：节点 + 连线 + 相机/视图绑定。</summary>
    [DataContract]
    public sealed class FlowDefinition
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        /// <summary>绑定的相机 Id（空 = 未绑定）。</summary>
        [DataMember]
        public string CameraId { get; set; }

        /// <summary>绑定的显示视图序号（从 1 开始）。</summary>
        [DataMember]
        public int ViewIndex { get; set; }

        /// <summary>
        /// 产品型号标签；<c>""</c> 表示「通用 / 未分类」。
        /// 旧配置缺失该字段时序列化给 null，由
        /// <see cref="FlowSettings.UpgradeToCurrent"/> 归一化为 <c>""</c>。
        /// </summary>
        [DataMember]
        public string Product { get; set; }

        /// <summary>
        /// 是否启用（未启用的流程不进入「全部启动」的作用域，也不会被并行引擎启动）。
        /// <strong>默认 true</strong>。
        /// ⚠️ 序列化陷阱：<c>DataContractJsonSerializer</c> 对 JSON 中缺失的 <c>bool</c>
        /// 会给默认值 <c>false</c>，因此旧配置必须经
        /// <see cref="FlowSettings.UpgradeToCurrent"/>（SchemaVersion &lt; 3 时）显式置 true，
        /// 否则旧流程会全部变成「未启用」而跑不起来（共享知识 33.1）。
        /// </summary>
        [DataMember]
        public bool Enabled { get; set; }

        [DataMember]
        public List<FlowNode> Nodes { get; set; }

        [DataMember]
        public List<FlowLink> Links { get; set; }

        public FlowDefinition()
        {
            Id = "";
            Name = "";
            CameraId = "";
            ViewIndex = 1;
            Product = "";
            Enabled = true;
            Nodes = new List<FlowNode>();
            Links = new List<FlowLink>();
        }

        /// <summary>创建一条空流程（自动生成 Id，默认启用、产品为「通用」）。</summary>
        public static FlowDefinition CreateEmpty(string name)
        {
            return new FlowDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = string.IsNullOrEmpty(name) ? "流程" : name,
                CameraId = "",
                ViewIndex = 1,
                Product = "",
                Enabled = true,
                Nodes = new List<FlowNode>(),
                Links = new List<FlowLink>()
            };
        }

        /// <summary>按 Id 查找节点；不存在返回 null。</summary>
        public FlowNode FindNode(string nodeId)
        {
            if (Nodes == null || string.IsNullOrEmpty(nodeId)) return null;
            foreach (FlowNode n in Nodes)
            {
                if (n != null && n.Id == nodeId) return n;
            }
            return null;
        }

        // ---------------------------------------------------------------------
        //  连线辅助查询（只读，不新增 [DataMember]，不影响序列化形态）
        // ---------------------------------------------------------------------

        /// <summary>所有指向 <paramref name="nodeId"/> 的连线；无连线或 id 为空时返回空列表。</summary>
        /// <param name="nodeId">目标节点 Id。</param>
        /// <param name="portKey">目标输入端子 Key；传 null / 空表示不筛选端子。</param>
        public List<FlowLink> FindLinksTo(string nodeId, string portKey = null)
        {
            var result = new List<FlowLink>();
            if (Links == null || string.IsNullOrEmpty(nodeId)) return result;

            bool filterPort = !string.IsNullOrEmpty(portKey);
            foreach (FlowLink l in Links)
            {
                if (l == null) continue;
                if (!string.Equals(l.ToNodeId, nodeId, StringComparison.Ordinal)) continue;
                if (filterPort && !string.Equals(l.ToPort ?? "", portKey, StringComparison.Ordinal)) continue;
                result.Add(l);
            }
            return result;
        }

        /// <summary>所有从 <paramref name="nodeId"/> 出发的连线；无连线或 id 为空时返回空列表。</summary>
        /// <param name="nodeId">源节点 Id。</param>
        /// <param name="portKey">源输出端子 Key；传 null / 空表示不筛选端子。</param>
        public List<FlowLink> FindLinksFrom(string nodeId, string portKey = null)
        {
            var result = new List<FlowLink>();
            if (Links == null || string.IsNullOrEmpty(nodeId)) return result;

            bool filterPort = !string.IsNullOrEmpty(portKey);
            foreach (FlowLink l in Links)
            {
                if (l == null) continue;
                if (!string.Equals(l.FromNodeId, nodeId, StringComparison.Ordinal)) continue;
                if (filterPort && !string.Equals(l.FromPort ?? "", portKey, StringComparison.Ordinal)) continue;
                result.Add(l);
            }
            return result;
        }

        /// <summary><paramref name="nodeId"/> 是否至少有一条入边（可选按输入端子筛选）。</summary>
        /// <param name="nodeId">目标节点 Id。</param>
        /// <param name="portKey">目标输入端子 Key；传 null / 空表示不限端子。</param>
        public bool HasLinkTo(string nodeId, string portKey = null)
        {
            return FindLinksTo(nodeId, portKey).Count > 0;
        }

        public FlowDefinition Clone()
        {
            var copy = new FlowDefinition
            {
                Id = Id,
                Name = Name,
                CameraId = CameraId,
                ViewIndex = ViewIndex,
                Product = Product ?? "",
                Enabled = Enabled,
                Nodes = new List<FlowNode>(),
                Links = new List<FlowLink>()
            };
            if (Nodes != null)
            {
                foreach (FlowNode n in Nodes) if (n != null) copy.Nodes.Add(n.Clone());
            }
            if (Links != null)
            {
                foreach (FlowLink l in Links) if (l != null) copy.Links.Add(l.Clone());
            }
            return copy;
        }

        /// <summary>
        /// 复制成一条<strong>可独立运行</strong>的新流程：新流程 Id、所有节点 Id 换新、连线端点按映射改写。
        /// <para>
        /// 【为什么节点 Id 也要换】模板资产按 <c>{flowId}\{nodeId}</c> 落盘，沿用旧 Id 会让两条流程
        /// 共用同一份模板 —— 改一个，另一个跟着变；日志/结果缓存/并行槽也都会串。
        /// </para>
        /// </summary>
        /// <param name="idMap">源节点 Id → 新节点 Id（复制模板资产时用）。</param>
        public FlowDefinition Duplicate(out Dictionary<string, string> idMap)
        {
            FlowDefinition copy = Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = Name + " 副本";

            idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (copy.Nodes != null)
            {
                foreach (FlowNode n in copy.Nodes)
                {
                    if (n == null) continue;
                    string oldId = n.Id;
                    n.Id = Guid.NewGuid().ToString("N");
                    if (!string.IsNullOrEmpty(oldId)) idMap[oldId] = n.Id;
                }
            }

            if (copy.Links != null)
            {
                foreach (FlowLink l in copy.Links)
                {
                    if (l == null) continue;
                    string v;
                    if (!string.IsNullOrEmpty(l.FromNodeId) && idMap.TryGetValue(l.FromNodeId, out v))
                        l.FromNodeId = v;
                    if (!string.IsNullOrEmpty(l.ToNodeId) && idMap.TryGetValue(l.ToNodeId, out v))
                        l.ToNodeId = v;
                }
            }

            return copy;
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? "未命名流程" : Name;
        }
    }

    /// <summary>流程配置段（写入 appconfig.json）。</summary>
    [DataContract]
    public sealed class FlowSettings
    {
        /// <summary>
        /// 当前 Schema 版本。
        /// 1 = 只有 8 类节点；
        /// 2 = 追加模板匹配 / 位置修正 / 直线卡尺 / 圆卡尺 4 类节点；
        /// 3 = 追加 <see cref="FlowDefinition.Product"/> / <see cref="FlowDefinition.Enabled"/>；
        /// 4 = 连线端子化：<see cref="FlowLink.FromPort"/> / <see cref="FlowLink.ToPort"/>。
        /// 每次升级都是<strong>纯追加</strong>（新类型用新字符串、新字段取默认值），
        /// 因此无需改写旧数据，旧配置不得加载失败。
        /// </summary>
        public const int CurrentSchemaVersion = 4;

        /// <summary>引入 <c>Product</c>/<c>Enabled</c> 的版本号（用于旧配置兼容性判断）。</summary>
        private const int VersionProductAndEnabled = 3;

        /// <summary>
        /// 引入连线端子（<c>FromPort</c>/<c>ToPort</c>）的版本号。
        /// ⚠️ 本版本号<strong>故意不作为归一化循环的前置门禁</strong>：
        /// 「v3 升上来」与「v4 配置被人手工改残」两种情况在内存里长得一模一样
        /// （都是缺失字段的 null），加了门禁反而会把后者漏掉。
        /// 归一化本身幂等，无条件兜底补一遍的代价极低。
        /// </summary>
        private const int VersionPorts = 4;


        [DataMember]
        public int SchemaVersion { get; set; }

        [DataMember]
        public string ActiveFlowId { get; set; }

        [DataMember]
        public List<FlowDefinition> Flows { get; set; }

        public FlowSettings()
        {
            SchemaVersion = CurrentSchemaVersion;
            ActiveFlowId = "";
            Flows = new List<FlowDefinition>();
        }

        /// <summary>
        /// 就地升级到当前 Schema：补齐版本号；为每条缺端子的连线补默认端子（Schema v4）；
        /// 并为每个节点补齐其类型在 <see cref="FlowModule"/> 中声明、但当前缺失的参数键（按默认值填充）。
        /// </summary>
        /// <remarks>
        /// 幂等且安全：只补不删。旧配置里只有 8 类节点、参数键较少时，
        /// 缺失键在运行时本身也会走 <see cref="FlowNode.GetDouble(string, double)"/> 的默认值，
        /// 本方法只是把默认值显式落盘，让界面参数面板有值可显示。
        /// 连线端子同理走「缺失即补」，非法值（表里查不到的 Key）也会被换成默认端子。
        /// </remarks>
        /// <returns>是否发生了改动（true 时调用方应保存配置）。</returns>
        public static bool UpgradeToCurrent(FlowSettings fs)
        {
            if (fs == null) return false;

            // 【旧配置兼容，请勿移除】先记住旧版本号：Enabled 是值类型 bool，
            // 反序列化缺失字段时本身就是 false，只有「旧版本升级」这一条路径能把它救回来。
            int oldVersion = fs.SchemaVersion;

            bool changed = fs.SchemaVersion != CurrentSchemaVersion;
            fs.SchemaVersion = CurrentSchemaVersion;

            if (fs.Flows == null) { fs.Flows = new List<FlowDefinition>(); changed = true; }

            foreach (FlowDefinition flow in fs.Flows)
            {
                if (flow == null) continue;

                // ---- Schema 2 → 3：Product / Enabled ----
                if (oldVersion < VersionProductAndEnabled)
                {
                    if (flow.Product == null) { flow.Product = ""; changed = true; }
                    if (!flow.Enabled) { flow.Enabled = true; changed = true; }
                }
                else if (flow.Product == null)
                {
                    flow.Product = "";
                    changed = true;
                }

                // ---- Schema 3 → 4：连线端子（FromPort / ToPort）----
                // 【无 oldVersion 门禁，请勿加】见 VersionPorts 的注释：null 值在
                // 「旧版本升级」与「v4 被手工改残」两种场景下无法区分，加了门禁会漏掉后者。
                // FlowLink.Normalize 幂等（只补不覆盖），重复调用无副作用。
                if (flow.Links == null) { flow.Links = new List<FlowLink>(); changed = true; }
                else
                {
                    foreach (FlowLink l in flow.Links)
                    {
                        if (l == null) continue;
                        if (FlowLink.Normalize(flow, l)) changed = true;
                    }
                }

                // ---- 既有：节点缺失参数键补默认值，保持不变 ----
                if (flow.Nodes == null) continue;

                foreach (FlowNode node in flow.Nodes)
                {
                    if (node == null) continue;

                    FlowNodeType type;
                    if (!Enum.TryParse(node.Type, out type)) continue;   // 未知类型：原样保留

                    FlowModuleInfo m = FlowModule.FindByType(type);
                    if (m == null) continue;

                    List<ParamItem> defs = m.CreateDefaultParams();
                    foreach (ParamItem d in defs)
                    {
                        if (d == null || string.IsNullOrEmpty(d.Key)) continue;
                        if (node.FindParam(d.Key) != null) continue;

                        node.Set(d.Key, d.Value);
                        changed = true;
                    }

                    // ---- OCR 模板目录：把上一版的 %AppData%\...\OcrChars 拉回程序目录 ----
                    // 【为什么必须迁移】上一版把新建节点的默认目录写成了**绝对路径**
                    // %AppData%\VisionPlatform\OcrChars。用户现在要求模板统一放在
                    // <程序目录>\OCRModel，若只改"新建默认值"，老节点仍指向 AppData，
                    // 现场就会看到"模板明明放进 OCRModel 了，识别还说库为空"。
                    if (type == FlowNodeType.Ocr && MigrateOcrTemplateDir(node)) changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// 把 OCR 节点的 <c>TemplateDir</c> 从上一版的 <c>...\OcrChars</c> 绝对路径迁到相对路径 <c>"OCRModel"</c>。
        /// <para>
        /// 只动**旧默认值**（目录名恰为 <c>OcrChars</c> 的绝对路径）；用户自己指定的其它绝对/相对路径
        /// 一律不碰 —— 迁移绝不能变成"把用户的自定义目录改掉"。
        /// </para>
        /// </summary>
        /// <returns>是否发生变更。</returns>
        public static bool MigrateOcrTemplateDir(FlowNode node)
        {
            if (node == null) return false;

            string v = node.GetString("TemplateDir", "OCRModel");
            v = (v ?? "").Trim();

            if (v.Length == 0)
            {
                node.Set("TemplateDir", "OCRModel");
                return true;
            }

            try
            {
                if (!System.IO.Path.IsPathRooted(v)) return false;

                string leaf = System.IO.Path.GetFileName(v.TrimEnd('\\', '/'));
                if (!string.Equals(leaf, "OcrChars", StringComparison.OrdinalIgnoreCase)) return false;

                node.Set("TemplateDir", "OCRModel");
                return true;
            }
            catch (Exception)
            {
                return false;   // 路径非法就原样保留，交给运行期报"目录不存在"
            }
        }
    }
}
