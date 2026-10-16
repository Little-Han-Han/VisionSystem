using System;
using System.Collections.Generic;
using System.IO;
using VisionPlatform.Flow;
using VisionPlatform.Vision;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 全局配置根对象，按三类参数清晰分区，统一持久化到 <see cref="AppPaths.ConfigFile"/>。
    ///   - System    : 系统参数（图片保存、通讯校验等全局开关）
    ///   - Algorithm : 算法参数（当前配方名 + 检测参数快照，保证二次打开软件即恢复）
    ///   - Ui        : 界面设置参数（布局、各视图的相机绑定）
    ///   - Flows     : 流程编排（流程定义、节点、连线、绑定关系）
    /// </summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class AppConfig
    {
        /// <summary>系统参数。</summary>
        [System.Runtime.Serialization.DataMember]
        public SystemSettings System { get; set; }

        /// <summary>算法参数。</summary>
        [System.Runtime.Serialization.DataMember]
        public AlgorithmSettings Algorithm { get; set; }

        /// <summary>界面设置参数。</summary>
        [System.Runtime.Serialization.DataMember]
        public UiSettings Ui { get; set; }

        /// <summary>流程编排参数。</summary>
        [System.Runtime.Serialization.DataMember]
        public FlowSettings Flows { get; set; }

        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                System = SystemSettings.CreateDefault(),
                Algorithm = new AlgorithmSettings(),
                Ui = new UiSettings(),
                Flows = new FlowSettings()
            };
        }
    }

    /// <summary>系统参数：图片保存相关与运行前置条件的全局开关。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class SystemSettings
    {
        /// <summary>图片保存根目录。</summary>
        [System.Runtime.Serialization.DataMember]
        public string ImageSavePath { get; set; }

        /// <summary>保存格式：PNG / JPEG / BMP / TIFF。</summary>
        [System.Runtime.Serialization.DataMember]
        public string ImageFormat { get; set; }

        /// <summary>图片保留天数；超过该天数的历史图片会被清理，0 表示不清理。</summary>
        [System.Runtime.Serialization.DataMember]
        public int RetentionDays { get; set; }

        /// <summary>每帧检测后自动保存图像。</summary>
        [System.Runtime.Serialization.DataMember]
        public bool AutoSaveEnabled { get; set; }

        /// <summary>自动保存时仅保存 NG 图像。</summary>
        [System.Runtime.Serialization.DataMember]
        public bool SaveNgOnly { get; set; }

        /// <summary>循环运行前是否要求通讯已连接（TCP 客户端 / Modbus / TCP 服务端）。</summary>
        [System.Runtime.Serialization.DataMember]
        public bool RequireCommBeforeRun { get; set; }

        /// <summary>
        /// 是否允许多条流程共享同一台相机（P2 预留，默认 false）。
        /// 为 false 时相机租约严格独占：第二条流程申请同一相机会被拒绝并给出中文原因。
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public bool AllowCameraShare { get; set; }

        /// <summary>
        /// 良品率告警下限（百分数，PRD FR-UI-05）。低于该值良率数字转橙，低于其一半转红；
        /// 小于等于 0 表示不做告警着色。默认 95。
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public double YieldAlarmPercent { get; set; }

        /// <summary>良品率告警下限的默认值（旧配置缺失该字段时补齐用）。</summary>
        public const double DefaultYieldAlarmPercent = 95.0;

        public static SystemSettings CreateDefault()
        {
            return new SystemSettings
            {
                ImageSavePath = Path.Combine(AppPaths.Root, "Images"),
                ImageFormat = "PNG",
                RetentionDays = 30,
                AutoSaveEnabled = false,
                SaveNgOnly = false,
                RequireCommBeforeRun = false,
                YieldAlarmPercent = DefaultYieldAlarmPercent,
                AllowCameraShare = false
            };
        }

        public SystemSettings Clone()
        {
            return new SystemSettings
            {
                ImageSavePath = ImageSavePath,
                ImageFormat = ImageFormat,
                RetentionDays = RetentionDays,
                AutoSaveEnabled = AutoSaveEnabled,
                SaveNgOnly = SaveNgOnly,
                RequireCommBeforeRun = RequireCommBeforeRun,
                YieldAlarmPercent = YieldAlarmPercent,
                AllowCameraShare = AllowCameraShare
            };
        }
    }

    /// <summary>算法参数：记录上次使用的配方名，并缓存一份检测参数快照，确保二次打开即恢复。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class AlgorithmSettings
    {
        /// <summary>上次生效的配方名（对应 Recipes 目录下的文件名，空表示无命名配方）。</summary>
        [System.Runtime.Serialization.DataMember]
        public string CurrentRecipeName { get; set; }

        /// <summary>检测参数快照（来自当前配方的 <see cref="InspectionConfig"/>）。</summary>
        [System.Runtime.Serialization.DataMember]
        public InspectionConfig Inspection { get; set; }

        public AlgorithmSettings()
        {
            CurrentRecipeName = "";
            Inspection = null;
        }

        public AlgorithmSettings Clone()
        {
            return new AlgorithmSettings
            {
                CurrentRecipeName = CurrentRecipeName,
                Inspection = Inspection != null ? Inspection.Clone() : null
            };
        }
    }

    /// <summary>界面设置参数：主界面布局与各显示视图的相机绑定。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class UiSettings
    {
        /// <summary>布局预设索引（对应 MainForm 的 LayoutPresets）。</summary>
        [System.Runtime.Serialization.DataMember]
        public int LayoutIndex { get; set; }

        /// <summary>每个视图绑定的相机 Id（空字符串 = 自动分配），下标即视图序号。</summary>
        [System.Runtime.Serialization.DataMember]
        public List<string> ViewBindings { get; set; }

        /// <summary>底部日志区是否折叠（PRD FR-UI-09）。</summary>
        [System.Runtime.Serialization.DataMember]
        public bool LogCollapsed { get; set; }

        /// <summary>
        /// 当前产品筛选（"" 或 null = 全部）。只影响显示与「全部启动」的作用域，
        /// 绝不删除数据、绝不改写流程的 <c>Product</c>（共享知识 34）。
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public string CurrentProduct { get; set; }

        /// <summary>
        /// 界面语言：<c>"zh-CN"</c>（默认）或 <c>"en"</c>，取值见 <see cref="Lang"/>。
        /// 运行时热切换，无需重启软件。
        /// <para>【用 string 不用 enum】DataContractJsonSerializer 会把 enum 序列化成数字，
        /// 配置文件里出现 <c>"Language":1</c> 这种不可读的值，排查问题很痛苦；
        /// 项目里 <see cref="SystemSettings.ImageFormat"/> 存 "PNG" 字符串就是先例。</para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public string Language { get; set; }

        /// <summary>
        /// 最大并行流程数（默认 4，合法范围 1~8）。越界值由
        /// <see cref="ClampMaxParallelFlows"/> 与 <see cref="ConfigStore.Load"/> 纠正。
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public int MaxParallelFlows { get; set; }

        /// <summary>
        /// 界面主题模式："Dark" / "Light"（大小写不敏感，其余值一律按 Dark 处理）。
        /// 由 <see cref="UiThemeManager"/> 写入，<see cref="Program.Main"/> 在首个窗体
        /// 创建之前读取并套用。
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public string Theme { get; set; }

        /// <summary>默认主题（深色）。</summary>
        public const string DefaultTheme = "Dark";

        /// <summary>最大并行流程数的默认值。</summary>
        public const int DefaultMaxParallelFlows = 4;

        /// <summary>最大并行流程数的下界。</summary>
        public const int MinParallelFlows = 1;

        /// <summary>最大并行流程数的上界。</summary>
        public const int MaxParallelFlowsLimit = 8;

        public UiSettings()
        {
            LayoutIndex = 0;
            ViewBindings = new List<string>();
            LogCollapsed = false;
            CurrentProduct = "";
            MaxParallelFlows = DefaultMaxParallelFlows;
            Language = Lang.ZhCN;
        }

        /// <summary>把最大并行流程数钳制到 1~8（0 / 99 之类的越界值都会被纠正）。
        /// 用于<strong>用户输入</strong>：0 只会被抬到下界 1，保留"用户就是想设 1"的语义。</summary>
        public static int ClampMaxParallelFlows(int value)
        {
            if (value < MinParallelFlows) return MinParallelFlows;
            if (value > MaxParallelFlowsLimit) return MaxParallelFlowsLimit;
            return value;
        }

        /// <summary>
        /// 归一化「反序列化得来的」最大并行流程数：0 / 负数视为字段缺失，补默认值 4；
        /// 其余交给 <see cref="ClampMaxParallelFlows"/>。
        /// <para>【与 Clamp 的分工】旧配置里根本没有该字段 → DataContract 反序列化给 0，
        /// 若直接 Clamp 会变成 1（一次只允许跑一条流程，等于并行功能被悄悄关掉），
        /// 这不是用户想要的。缺失必须补默认 4，只有显式写出来的越界值才 Clamp。</para>
        /// </summary>
        public static int NormalizeMaxParallelFlows(int value)
        {
            if (value <= 0) return DefaultMaxParallelFlows;
            return ClampMaxParallelFlows(value);
        }

        public UiSettings Clone()
        {
            var clone = new UiSettings();
            clone.LayoutIndex = LayoutIndex;
            clone.LogCollapsed = LogCollapsed;
            // 缺失值（0 → 4）比"抬到 1"更符合预期，故 Clone 也走归一化
            clone.CurrentProduct = CurrentProduct ?? "";
            clone.MaxParallelFlows = NormalizeMaxParallelFlows(MaxParallelFlows);
            // 缺失值归一化为默认语言；Lang.Normalize 会在保存端再做一次兜底
            clone.Language = string.IsNullOrWhiteSpace(Language) ? Lang.ZhCN : Language;
            if (ViewBindings != null) clone.ViewBindings = new List<string>(ViewBindings);
            return clone;
        }
    }

    /// <summary>
    /// 全局配置的加载 / 保存与全局访问。启动时 <see cref="Load"/>，界面保存时 <see cref="Save"/>。
    /// 配置按系统 / 算法 / 界面三类分区存放于同一个 JSON 文件（<see cref="AppPaths.ConfigFile"/>）。
    /// </summary>
    public static class ConfigStore
    {
        private static AppConfig _current;

        /// <summary>当前配置（首次访问时自动从磁盘加载）。</summary>
        public static AppConfig Current
        {
            get { return _current ?? (_current = Load()); }
        }

        /// <summary>配置被保存后触发（界面据此刷新状态）。</summary>
        public static event EventHandler Changed;

        /// <summary>从配置文件加载；文件不存在或字段缺失时按默认值补齐。</summary>
        public static AppConfig Load()
        {
            AppConfig cfg = null;

            try
            {
                cfg = Json.LoadFromFile<AppConfig>(AppPaths.ConfigFile);
            }
            catch (Exception ex)
            {
                Logger.Warn("读取配置失败，使用默认值：" + ex.Message);
            }

            if (cfg == null) cfg = AppConfig.CreateDefault();

            // 兼容旧配置 / 字段缺失：逐段补齐
            if (cfg.System == null) cfg.System = SystemSettings.CreateDefault();
            if (cfg.Algorithm == null) cfg.Algorithm = new AlgorithmSettings();
            if (cfg.Ui == null) cfg.Ui = new UiSettings();
            if (cfg.Ui.ViewBindings == null) cfg.Ui.ViewBindings = new List<string>();
            if (cfg.Flows == null) cfg.Flows = new FlowSettings();
            if (cfg.Flows.Flows == null) cfg.Flows.Flows = new List<FlowDefinition>();

            // 首次运行 / 旧配置没有流程时，补一条空流程并设为当前流程
            if (cfg.Flows.Flows.Count == 0)
            {
                FlowDefinition f = FlowDefinition.CreateEmpty("流程 1");
                cfg.Flows.Flows.Add(f);
                cfg.Flows.ActiveFlowId = f.Id;
            }

            // 【旧配置兼容，请勿移除 —— 这是「旧配置还能不能跑起来」的唯一保险】
            // SchemaVersion < 3 的旧配置里没有 Product / Enabled 两个字段，而
            // DataContractJsonSerializer 对 JSON 中缺失的 bool 只会给默认值 false，
            // 反序列化后所有旧流程的 Enabled 都是 false。
            // 若不在加载时就地升级，并行引擎（FlowRunManager.StartAll）会把全部旧流程
            // 当成「已停用」，表现为「升级版本后一条流程都启动不了」。
            // UpgradeToCurrent 是幂等的（只补不删），重复调用无副作用；
            // FlowForm 打开时会再调一次并把结果落盘。
            if (FlowSettings.UpgradeToCurrent(cfg.Flows))
            {
                Logger.Info("流程配置已就地升级到 Schema v" + FlowSettings.CurrentSchemaVersion
                            + "（补齐 Product / Enabled 等新字段默认值）");
            }

            // 当前流程 Id 无效时回退到第一条
            if (string.IsNullOrEmpty(cfg.Flows.ActiveFlowId)
                || cfg.Flows.Flows.Find(x => x != null && x.Id == cfg.Flows.ActiveFlowId) == null)
            {
                cfg.Flows.ActiveFlowId = cfg.Flows.Flows[0].Id;
            }

            if (string.IsNullOrWhiteSpace(cfg.System.ImageSavePath))
                cfg.System.ImageSavePath = Path.Combine(AppPaths.Root, "Images");
            if (string.IsNullOrWhiteSpace(cfg.System.ImageFormat)) cfg.System.ImageFormat = "PNG";
            if (cfg.System.RetentionDays < 0) cfg.System.RetentionDays = 30;
            if (cfg.Ui.LayoutIndex < 0) cfg.Ui.LayoutIndex = 0;

            // 【旧配置兼容，请勿移除】旧版 appconfig.json 没有 YieldAlarmPercent 字段，
            // 反序列化后为 0，这里补齐默认值 95，保证良率告警着色行为与首次运行一致。
            if (cfg.System.YieldAlarmPercent <= 0.0)
                cfg.System.YieldAlarmPercent = SystemSettings.DefaultYieldAlarmPercent;

            // 【旧配置兼容，请勿移除】旧版 appconfig.json 没有这三项：
            // - CurrentProduct 缺失即 null → 归一化为 ""（= 全部）
            // - MaxParallelFlows 缺失即 0 → 补默认值 4（不能 Clamp，否则 0→1 等于关掉并行）
            // - AllowCameraShare 缺失即 false → 正是默认值（严格独占），无需额外处理
            if (cfg.Ui.CurrentProduct == null) cfg.Ui.CurrentProduct = "";
            cfg.Ui.MaxParallelFlows = UiSettings.NormalizeMaxParallelFlows(cfg.Ui.MaxParallelFlows);

            // 【旧配置兼容，请勿移除】旧版 appconfig.json 没有 Language 字段，
            // 反序列化后为 null，这里归一化为默认语言 zh-CN。
            // 刻意不"钳制"未知代码（如手改出的 "jp"）：语言代码是开放集合，
            // 钳成 zh-CN 会把用户手改的值静默抹掉，交由 Lang.Normalize 在读取端兜底即可。
            if (string.IsNullOrWhiteSpace(cfg.Ui.Language)) cfg.Ui.Language = Lang.ZhCN;

            _current = cfg;
            return cfg;
        }

        /// <summary>保存配置并广播变更。</summary>
        public static void Save(AppConfig config)
        {
            if (config == null) return;

            _current = config;

            try
            {
                Json.SaveToFile(AppPaths.ConfigFile, config);
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败：" + ex.Message);
            }

            EventHandler handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        /// <summary>保存当前配置（便捷重载）。</summary>
        public static void Save()
        {
            Save(Current);
        }
    }
}
