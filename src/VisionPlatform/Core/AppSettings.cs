using System;
using System.Collections.Generic;
using System.IO;
using VisionPlatform.Calibration;
using VisionPlatform.Recipe;
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

        /// <summary>
        /// 通讯管理参数：用户自定义的多条<strong>命名</strong>通讯（TCP 客户端 / TCP 服务端 / Modbus TCP）。
        /// 流程节点（发送 / 接收数据）按名称绑定这里的一条通讯，运行时由
        /// Communication 目录下的 CommRegistry 持有对应的活连接对象。
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public CommSettings Comm { get; set; }

        /// <summary>
        /// 相机标定参数：每台相机的多条 <see cref="CalibRecord"/>（名称 / 类型 / RMS / 变换参数），
        /// 以及「相机 → 当前生效记录」的映射。框选与定位算法按此把像素坐标换算成机械坐标。
        /// <para>
        /// 【旧配置兼容】旧版 appconfig.json 没有本段，反序列化得 null，
        /// 由 <see cref="ConfigStore.Load"/> 统一归一化为默认空配置（见那里的注释）。
        /// </para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public CalibSettings Calibrations { get; set; }

        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                System = SystemSettings.CreateDefault(),
                Algorithm = new AlgorithmSettings(),
                Ui = new UiSettings(),
                Flows = new FlowSettings(),
                Comm = CommSettings.CreateDefault(),
                Calibrations = CalibSettings.CreateDefault()
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

        /// <summary>
        /// 日志数据库文件路径（SQLite）。为空时回落到默认 <see cref="AppPaths.LogDbFile"/>。
        /// 一般无需改动。
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public string DbPath { get; set; }

        /// <summary>
        /// 日志保留天数；超过该天数的日志记录（含 OK/NG 判定与软件报错）会被清理，
        /// 0 表示不清理。默认 90。
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public int LogRetentionDays { get; set; }

        /// <summary>良品率告警下限的默认值（旧配置缺失该字段时补齐用）。</summary>
        public const double DefaultYieldAlarmPercent = 95.0;

        /// <summary>日志保留天数默认值（旧配置缺失该字段时补齐用）。</summary>
        public const int DefaultLogRetentionDays = 90;

        /// <summary>触发式运行：「运行」后只建立通讯与流程、<strong>等外部触发</strong>才跑一轮（产线模式）。</summary>
        public const string RunModeTriggered = "Triggered";

        /// <summary>连续运行：「运行」后流程按帧率一直跑（调试 / 无触发源的场景）。</summary>
        public const string RunModeContinuous = "Continuous";

        /// <summary>
        /// 运行模式：<see cref="RunModeTriggered"/>（默认）/ <see cref="RunModeContinuous"/>。
        /// <para>
        /// 【为什么用字符串而不是 bool】<c>DataContractJsonSerializer</c> 把缺失的 bool 反序列化成
        /// <c>false</c> —— 那会让所有老配置静默变成"连续运行"（正是要修的那个毛病）。
        /// 字符串缺失是 <c>null</c>，<see cref="NormalizeRunMode"/> 才能区分"没配过"与"显式选连续"。
        /// </para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public string RunMode { get; set; }

        /// <summary>规范化运行模式：null / 空 / 未知值一律回落到触发式。</summary>
        public static string NormalizeRunMode(string mode)
        {
            if (string.Equals(mode, RunModeContinuous, StringComparison.OrdinalIgnoreCase))
                return RunModeContinuous;
            return RunModeTriggered;
        }

        /// <summary>
        /// 授权校验模式：<c>Off</c>（默认，不校验）/ <c>Warn</c>（只记日志不拦截）/
        /// <c>Enforce</c>（未授权即拦截启动）。
        /// <para>
        /// 【默认 Off 是刻意为之】签发工具（LicenseTool）尚未产出正式公钥，
        /// 此时强制校验会让所有现场无法启动；等公钥内置后由部署方改成 Enforce。
        /// </para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public string LicenseMode { get; set; }

        /// <summary>当前配置是否触发式运行（产线握手模式）。</summary>
        public bool IsTriggeredRun
        {
            get { return NormalizeRunMode(RunMode) == RunModeTriggered; }
        }

        /// <summary>授权模式归一化：空/未知一律 Enforce（宁严不松）。</summary>
        public static string NormalizeLicenseMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode)) return "Enforce";
            string m = mode.Trim();
            if (string.Equals(m, "Off", StringComparison.OrdinalIgnoreCase)) return "Off";
            if (string.Equals(m, "Warn", StringComparison.OrdinalIgnoreCase)) return "Warn";
            return "Enforce";
        }

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
                AllowCameraShare = false,
                DbPath = "",
                LogRetentionDays = DefaultLogRetentionDays,
                RunMode = RunModeTriggered,
                LicenseMode = "Enforce"      // 正式发版：无有效许可证不允许启动（Off/Warn 留给调试）
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
                AllowCameraShare = AllowCameraShare,
                DbPath = DbPath,
                LogRetentionDays = LogRetentionDays,
                RunMode = NormalizeRunMode(RunMode),
                LicenseMode = NormalizeLicenseMode(LicenseMode)
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

        /// <summary>
        /// 单条流程可用的算法并行线程数；<c>0</c> = 自动（核数 / 在跑流程数，上限 4）。
        /// <para>
        /// 【为什么需要这个开关】形状匹配用 <c>#pragma omp parallel for</c>，OpenMP 默认按物理核数
        /// 开满线程。宿主本来就是每条流程一个线程，于是小图上并行开销远大于收益：
        /// 实测同一条流程 12 线程 → 8.3 fps / CPU 11.5 核，2 线程 → 3.4 fps / CPU 2.14 核
        /// （每帧的 CPU 成本从 1.4 核·s 降到 0.63 核·s，能效提高一倍多）。
        /// 要极限帧率就调大（8/12），要低占用就调小（1/2）。
        /// </para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public int MaxThreads { get; set; }

        /// <summary>自动模式的并行度上限：再多只会把 CPU 烧在同步上。</summary>
        public const int AutoThreadsCap = 4;

        public AlgorithmSettings()
        {
            CurrentRecipeName = "";
            Inspection = null;
            MaxThreads = 0;
        }

        public AlgorithmSettings Clone()
        {
            return new AlgorithmSettings
            {
                CurrentRecipeName = CurrentRecipeName,
                Inspection = Inspection != null ? Inspection.Clone() : null,
                MaxThreads = MaxThreads
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
        /// 单条流程的循环运行目标帧率（0 = 不限制，最大 1000）。
        /// <para>
        /// 【为什么默认 0】旧版把限帧硬编码为 33ms（≈30fps），算法只要快于 33ms 就会被
        /// 人工压到 30fps，看起来"比 Halcon 慢"。现在默认不限制，按相机/算法实际能力跑；
        /// 需要控制 CPU 或日志量时把它设成 30 / 60 即可。
        /// </para>
        /// <para>只影响 MainForm 的「全部启动」与 FlowForm 的连续运行，不影响单帧/单步。</para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public int MaxFlowFps { get; set; }

        /// <summary>
        /// 界面主题模式："Dark" / "Light"（大小写不敏感，其余值一律按 Dark 处理）。
        /// 由 <see cref="UiThemeManager"/> 写入，<see cref="Program.Main"/> 在首个窗体
        /// 创建之前读取并套用。
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public string Theme { get; set; }

        /// <summary>默认主题（深色）。</summary>
        public const string DefaultTheme = "Dark";

        /// <summary>
        /// 结果区高度（<strong>96-DPI 逻辑像素</strong>，默认 180）。
        /// <para>
        /// 【为什么存逻辑值不存物理像素】150% DPI 下若直接存物理高度，换显示器/改缩放后
        /// 重启会"结果区突然变成一半高"。显示时按 <c>实际px = 逻辑值 × DeviceDpi / 96</c> 换算。
        /// </para>
        /// <para>【0 表示"字段缺失"】<c>DataContractJsonSerializer</c> 反序列化不跑构造函数，
        /// 老配置没有这个字段时读到 0；120 才是合法下界，所以"0 = 未设置"天然可区分（§C6）。</para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public int FlowResultPanelHeight { get; set; }

        /// <summary>
        /// 端子区展开状态：0 = 未设置（取默认收起）、1 = 收起、2 = 展开。
        /// <para>
        /// 【为什么是 int 不是 bool】需求是"默认收起"，而 <c>DataContractJsonSerializer</c>
        /// 对缺失的 bool 一律给 <c>false</c> —— 用 bool 的话"字段缺失"与"用户显式展开"无法区分，
        /// 老配置会表现为"默认展开"，与需求正好相反。三态 int 把"缺失"独立表达出来。
        /// </para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public int FlowPortsState { get; set; }

        /// <summary>
        /// 画布叠加标签的详细度：1 = 简（只标序号）/ 2 = 标准（序号 + 分数）/ 3 = 详细（再补坐标与尺寸）。
        /// <para>【为什么是 1~3 而不是 0~2】<c>DataContractJsonSerializer</c> 对缺失的 int 给 0，
        /// 0 必须能表达"字段缺失"，否则无法把"老配置"与"用户选了简"区分开。</para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public int FlowLabelDetail { get; set; }

        /// <summary>标注详细度：简。</summary>
        public const int LabelDetailSimple = 1;

        /// <summary>标注详细度：标准（默认）。</summary>
        public const int LabelDetailStandard = 2;

        /// <summary>标注详细度：详细。</summary>
        public const int LabelDetailDetailed = 3;

        /// <summary>归一化标注详细度：缺失（0）或越界一律按"标准"。</summary>
        public static int NormalizeLabelDetail(int value)
        {
            if (value == LabelDetailSimple || value == LabelDetailDetailed) return value;
            return LabelDetailStandard;
        }

        /// <summary>
        /// 结果区高度默认值（逻辑像素）。
        /// <para>
        /// 【为什么从 180 提到 260】右栏只有 300px 宽，"一节点一行 + 展开明细"的形态下
        /// 180px 只够放主行 + 5 条明细（BLOB / 形状匹配动辄十几个目标），现场看就是"结果不全"。
        /// 260 可稳定放下主行 + 8 条；再多靠滚动（行区本身 AutoScroll）。
        /// </para>
        /// </summary>
        public const int DefaultResultPanelHeight = 260;

        /// <summary>
        /// 右栏宽度（<strong>逻辑像素</strong>，默认 400）。
        /// <para>右栏要同时装"参数表 + 结果表格"：300px 宽时表格三列必然挤，
        /// 所以给一条可拖的竖直分隔条，并把宽度记在配置里。</para>
        /// </summary>
        [System.Runtime.Serialization.DataMember]
        public int FlowRightWidth { get; set; }

        /// <summary>右栏宽度默认值（逻辑像素）。</summary>
        public const int DefaultRightWidth = 400;

        /// <summary>右栏宽度下界（再窄表格就没法看了）。</summary>
        public const int MinRightWidth = 320;

        /// <summary>右栏宽度上界（再宽画布就没了）。</summary>
        public const int MaxRightWidth = 680;

        /// <summary>归一化右栏宽度：缺失（0）补默认，越界夹到 320~680。</summary>
        public static int NormalizeRightWidth(int value)
        {
            if (value <= 0) return DefaultRightWidth;
            if (value < MinRightWidth) return MinRightWidth;
            if (value > MaxRightWidth) return MaxRightWidth;
            return value;
        }

        /// <summary>结果区高度下界（逻辑像素）：至少放得下主行 + 4 条明细。</summary>
        public const int MinResultPanelHeight = 180;

        /// <summary>结果区高度上界（逻辑像素）。</summary>
        public const int MaxResultPanelHeight = 460;

        /// <summary>端子区状态：未设置。</summary>
        public const int PortsStateUnset = 0;

        /// <summary>端子区状态：收起。</summary>
        public const int PortsStateCollapsed = 1;

        /// <summary>端子区状态：展开。</summary>
        public const int PortsStateExpanded = 2;

        /// <summary>
        /// 归一化「反序列化得来的」结果区高度：&lt;= 0 视为字段缺失补默认 180；
        /// 其余夹到 120~380。
        /// <para>【与"钳制"的区别】缺失必须补默认值，只有显式写出的越界值才夹 ——
        /// 把缺失值钳到 120 会让老用户一升级就发现"结果区变矮了"。</para>
        /// </summary>
        public static int NormalizeResultPanelHeight(int value)
        {
            if (value <= 0) return DefaultResultPanelHeight;
            if (value < MinResultPanelHeight) return MinResultPanelHeight;
            if (value > MaxResultPanelHeight) return MaxResultPanelHeight;
            return value;
        }

        /// <summary>归一化端子区状态：只接受 1 / 2，其余（含缺失的 0）一律按默认"收起"。</summary>
        public static int NormalizePortsState(int value)
        {
            return value == PortsStateExpanded ? PortsStateExpanded : PortsStateCollapsed;
        }

        /// <summary>最大并行流程数的默认值。</summary>
        public const int DefaultMaxParallelFlows = 4;

        /// <summary>循环运行目标帧率默认值：0 = 不限制。</summary>
        public const int DefaultMaxFlowFps = 0;

        /// <summary>目标帧率上界（防止手改配置写出 1e9 之类的值）。</summary>
        public const int MaxFlowFpsLimit = 1000;

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
            MaxFlowFps = DefaultMaxFlowFps;
            Language = Lang.ZhCN;
            Theme = DefaultTheme;
            FlowResultPanelHeight = DefaultResultPanelHeight;
            FlowPortsState = PortsStateCollapsed;
            FlowLabelDetail = LabelDetailStandard;
            FlowRightWidth = DefaultRightWidth;
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

        /// <summary>归一化目标帧率：负数按 0（不限制），超过 1000 按 1000。</summary>
        public static int NormalizeMaxFlowFps(int value)
        {
            if (value < 0) return 0;
            if (value > MaxFlowFpsLimit) return MaxFlowFpsLimit;
            return value;
        }

        public UiSettings Clone()
        {
            var clone = new UiSettings();
            clone.LayoutIndex = LayoutIndex;
            clone.LogCollapsed = LogCollapsed;
            // 缺失值（0 → 4）比"抬到 1"更符合预期，故 Clone 也走归一化
            clone.CurrentProduct = CurrentProduct ?? "";
            clone.MaxParallelFlows = NormalizeMaxParallelFlows(MaxParallelFlows);
            clone.MaxFlowFps = NormalizeMaxFlowFps(MaxFlowFps);
            // 缺失值归一化为默认语言；Lang.Normalize 会在保存端再做一次兜底
            clone.Language = string.IsNullOrWhiteSpace(Language) ? Lang.ZhCN : Language;
            // 主题同理：缺失即深色（漏掉这行会在保存时被清成 null，下次启动退回深色）
            clone.Theme = string.IsNullOrWhiteSpace(Theme) ? DefaultTheme : Theme;
            // 新增的两个 UI 偏好也必须归一化后带过去，否则保存时会把它们清成 0（= "未设置"），
            // 下次启动虽然能补默认值，但用户拖过的高度就白拖了。
            clone.FlowResultPanelHeight = NormalizeResultPanelHeight(FlowResultPanelHeight);
            clone.FlowPortsState = NormalizePortsState(FlowPortsState);
            // 标注详细度同理：归一化后再带过去，否则保存时会被清成 0（= 未设置）
            clone.FlowLabelDetail = NormalizeLabelDetail(FlowLabelDetail);
            clone.FlowRightWidth = NormalizeRightWidth(FlowRightWidth);
            if (ViewBindings != null) clone.ViewBindings = new List<string>(ViewBindings);
            return clone;
        }
    }

    /// <summary>
    /// 通讯端点类型常量。
    /// <para>【为什么是 string 而不是 enum】DataContractJsonSerializer 会把 enum 序列化成数字，
    /// 配置文件里就会出现 <c>"Kind":1</c> 这种不可读、手改即错的值；先例见
    /// <see cref="UiSettings.Language"/> 与 <see cref="SystemSettings.ImageFormat"/>。</para>
    /// </summary>
    public static class CommKind
    {
        /// <summary>TCP 客户端：主动连接远端（PLC / 上位机）。</summary>
        public const string TcpClient = "TcpClient";

        /// <summary>TCP 服务端：本地监听端口，把结果推给 MES / 上位机。</summary>
        public const string TcpServer = "TcpServer";

        /// <summary>Modbus TCP 客户端（0x03 / 0x06 / 0x10）。</summary>
        public const string ModbusTcp = "ModbusTcp";

        /// <summary>三种类型的固定顺序，下标即界面下拉框的 SelectedIndex。</summary>
        public static readonly string[] All = { TcpClient, TcpServer, ModbusTcp };

        /// <summary>界面显示名，下标与 <see cref="All"/> 严格对齐。</summary>
        public static readonly string[] DisplayNames = { "TCP 客户端", "TCP 服务端", "Modbus TCP" };

        /// <summary>是否内置协议（TCP / Modbus）；false 表示由 Communication\*.dll 插件提供。</summary>
        public static bool IsBuiltIn(string kind)
        {
            return kind == TcpClient || kind == TcpServer || kind == ModbusTcp;
        }

        /// <summary>归一化：未知 / 空值一律按 TCP 客户端处理。</summary>
        public static string Normalize(string kind)
        {
            if (kind == TcpClient || kind == TcpServer || kind == ModbusTcp) return kind;
            return TcpClient;
        }

        /// <summary>界面显示名。</summary>
        public static string Display(string kind)
        {
            string k = Normalize(kind);
            for (int i = 0; i < All.Length; i++)
                if (All[i] == k) return DisplayNames[i];
            return DisplayNames[0];
        }

        /// <summary>该类型的默认端口（Modbus 502，其余 9000）。</summary>
        public static int DefaultPort(string kind)
        {
            return Normalize(kind) == ModbusTcp ? 502 : 9000;
        }
    }

    /// <summary>
    /// 通讯管理参数：用户可自定义增删改名的多条通讯端点。
    /// 端点顺序即界面列表顺序，<see cref="CommEndpointConfig.Name"/> 在段内唯一（比较时忽略大小写）。
    /// </summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class CommSettings
    {
        /// <summary>通讯配置结构版本；<see cref="ConfigStore.Load"/> 据此做一次性升级。</summary>
        public const int CurrentSchemaVersion = 2;

        /// <summary>结构版本（序列化字段，勿删：删了就无法区分「用户停用」与「字段缺失」）。</summary>
        [System.Runtime.Serialization.DataMember]
        public int SchemaVersion { get; set; }

        /// <summary>通讯端点列表。</summary>
        [System.Runtime.Serialization.DataMember]
        public List<CommEndpointConfig> Endpoints { get; set; }

        /// <summary>点位表：名称 → 协议地址 / 类型 / 方向（协议无关）。</summary>
        [System.Runtime.Serialization.DataMember]
        public List<CommPointConfig> Points { get; set; }

        /// <summary>消息模板：结果 / 心跳 / 组合信息的文本、JSON 或多点写入。</summary>
        [System.Runtime.Serialization.DataMember]
        public List<CommMessageConfig> Messages { get; set; }

        /// <summary>常驻任务：心跳、结果推送、触发启动、状态镜像。</summary>
        [System.Runtime.Serialization.DataMember]
        public List<CommJobConfig> Jobs { get; set; }

        /// <summary>流程状态绑定：每条流程的 Busy / Ready / Fault 写到哪些点位。</summary>
        [System.Runtime.Serialization.DataMember]
        public List<CommFlowBindingConfig> FlowBindings { get; set; }

        public CommSettings()
        {
            SchemaVersion = CurrentSchemaVersion;
            Endpoints = new List<CommEndpointConfig>();
            Points = new List<CommPointConfig>();
            Messages = new List<CommMessageConfig>();
            Jobs = new List<CommJobConfig>();
            FlowBindings = new List<CommFlowBindingConfig>();
        }

        /// <summary>默认值：给出一条示例端点，用户可直接改名 / 改参数，也可删除。</summary>
        public static CommSettings CreateDefault()
        {
            CommSettings s = new CommSettings();
            s.Endpoints.Add(CommEndpointConfig.Create("TCP 客户端 1", CommKind.TcpClient));
            return s;
        }

        /// <summary>
        /// 就地升级并归一化，返回是否发生变更（幂等，可重复调用；先例 FlowSettings.UpgradeToCurrent）。
        /// <para>【为什么需要它】<see cref="CommEndpointConfig.Enabled"/> 的默认值应为 true，
        /// 而 DataContractJsonSerializer 对 JSON 中缺失的 bool 只给 false（铁律 5）。
        /// 手改过的配置若没有 Enabled 字段，整段通讯会被当成「已停用」，
        /// 表现为「建好了却一条都连不上」，必须在加载时显式补 true。</para>
        /// </summary>
        public static bool UpgradeToCurrent(CommSettings settings)
        {
            if (settings == null) return false;

            bool changed = false;

            if (settings.Endpoints == null)
            {
                settings.Endpoints = new List<CommEndpointConfig>();
                changed = true;
            }

            // v0 → v1：旧配置没有 Enabled 字段，补 true。
            // v1 → v2：只增加 Points / Messages / Jobs / FlowBindings，绝不改写用户已停用的端点。
            if (settings.SchemaVersion < 1)
            {
                foreach (CommEndpointConfig ep in settings.Endpoints)
                {
                    if (ep != null) ep.Enabled = true;
                }
            }

            if (settings.SchemaVersion < CurrentSchemaVersion)
            {
                settings.SchemaVersion = CurrentSchemaVersion;
                changed = true;
            }

            if (settings.Points == null) { settings.Points = new List<CommPointConfig>(); changed = true; }
            if (settings.Messages == null) { settings.Messages = new List<CommMessageConfig>(); changed = true; }
            if (settings.Jobs == null) { settings.Jobs = new List<CommJobConfig>(); changed = true; }
            if (settings.FlowBindings == null) { settings.FlowBindings = new List<CommFlowBindingConfig>(); changed = true; }

            foreach (CommEndpointConfig ep in settings.Endpoints)
            {
                if (ep != null) ep.Normalize();
            }
            foreach (CommPointConfig pt in settings.Points) if (pt != null) pt.Normalize();
            foreach (CommMessageConfig msg in settings.Messages) if (msg != null) msg.Normalize();
            foreach (CommJobConfig job in settings.Jobs) if (job != null) job.Normalize();
            foreach (CommFlowBindingConfig b in settings.FlowBindings) if (b != null) b.Normalize();

            return changed;
        }

        /// <summary>按名称查找（忽略大小写）；未找到返回 null。</summary>
        public CommEndpointConfig Find(string name)
        {
            if (string.IsNullOrEmpty(name) || Endpoints == null) return null;

            foreach (CommEndpointConfig ep in Endpoints)
            {
                if (ep != null && string.Equals(ep.Name, name, StringComparison.OrdinalIgnoreCase))
                    return ep;
            }

            return null;
        }

        /// <summary>名称是否已占用（忽略大小写）。<paramref name="excludeSelf"/> 用于改名时排除自身。</summary>
        public bool NameExists(string name, string excludeSelf)
        {
            if (string.IsNullOrEmpty(name) || Endpoints == null) return false;

            foreach (CommEndpointConfig ep in Endpoints)
            {
                if (ep == null) continue;
                if (!string.Equals(ep.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(excludeSelf)
                    && string.Equals(ep.Name, excludeSelf, StringComparison.OrdinalIgnoreCase)) continue;

                return true;
            }

            return false;
        }

        /// <summary>生成一个段内不重名的名称，形如「TCP 客户端 2」。</summary>
        public string MakeUniqueName(string baseName)
        {
            string b = string.IsNullOrWhiteSpace(baseName) ? "通讯" : baseName.Trim();

            for (int i = 1; i < 1000; i++)
            {
                string candidate = b + " " + i.ToString(Inv);
                if (!NameExists(candidate, null)) return candidate;
            }

            return b + " " + DateTime.Now.ToString("HHmmss", Inv);
        }

        /// <summary>固定用不变文化生成编号，避免不同区域设置下出现非阿拉伯数字。</summary>
        private static readonly System.Globalization.CultureInfo Inv =
            System.Globalization.CultureInfo.InvariantCulture;

        public CommSettings Clone()
        {
            CommSettings c = new CommSettings();
            c.SchemaVersion = SchemaVersion;
            if (Endpoints != null)
            {
                foreach (CommEndpointConfig ep in Endpoints)
                {
                    if (ep != null) c.Endpoints.Add(ep.Clone());
                }
            }
            if (Points != null) foreach (CommPointConfig pt in Points) if (pt != null) c.Points.Add(pt.Clone());
            if (Messages != null) foreach (CommMessageConfig m in Messages) if (m != null) c.Messages.Add(m.Clone());
            if (Jobs != null) foreach (CommJobConfig j in Jobs) if (j != null) c.Jobs.Add(j.Clone());
            if (FlowBindings != null) foreach (CommFlowBindingConfig b in FlowBindings) if (b != null) c.FlowBindings.Add(b.Clone());
            return c;
        }
    }

    /// <summary>
    /// 单条通讯端点的配置。一条端点 = 一个用户自定义名称 + 一种协议 + 各自参数。
    /// </summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class CommEndpointConfig
    {
        /// <summary>Modbus 从站号默认值。</summary>
        public const int DefaultSlaveId = 1;

        /// <summary>Modbus 从站号下界。</summary>
        public const int MinSlaveId = 1;

        /// <summary>Modbus 从站号上界（标准 Modbus 寻址上限）。</summary>
        public const int MaxSlaveId = 247;

        /// <summary>唯一名称：用户自定义，流程节点按它绑定通讯。</summary>
        [System.Runtime.Serialization.DataMember]
        public string Name { get; set; }

        /// <summary>协议类型，取值见 <see cref="CommKind"/>（字符串，不用 enum）。</summary>
        [System.Runtime.Serialization.DataMember]
        public string Kind { get; set; }

        /// <summary>目标主机 / IP；TCP 服务端不使用（本地监听）。</summary>
        [System.Runtime.Serialization.DataMember]
        public string Host { get; set; }

        /// <summary>端口；服务端为监听端口。</summary>
        [System.Runtime.Serialization.DataMember]
        public int Port { get; set; }

        /// <summary>Modbus 从站号，默认 1。</summary>
        [System.Runtime.Serialization.DataMember]
        public int SlaveId { get; set; }

        /// <summary>是否启用；停用的端点不允许连接。</summary>
        [System.Runtime.Serialization.DataMember]
        public bool Enabled { get; set; }

        /// <summary>软件启动时自动连接一次。</summary>
        [System.Runtime.Serialization.DataMember]
        public bool AutoConnect { get; set; }

        /// <summary>TCP 分帧：None / Newline / LengthPrefix / StxEtx。</summary>
        [System.Runtime.Serialization.DataMember]
        public string Framing { get; set; }

        /// <summary>单次读写超时（ms）。</summary>
        [System.Runtime.Serialization.DataMember]
        public int TimeoutMs { get; set; }

        /// <summary>断线重连间隔（ms）；0 = 不自动重连。</summary>
        [System.Runtime.Serialization.DataMember]
        public int ReconnectMs { get; set; }

        public CommEndpointConfig()
        {
            Name = "";
            Kind = CommKind.TcpClient;
            Host = "127.0.0.1";
            Port = CommKind.DefaultPort(CommKind.TcpClient);
            SlaveId = DefaultSlaveId;
            Enabled = true;
            AutoConnect = false;
            Framing = "None";
            TimeoutMs = 1000;
            ReconnectMs = 3000;
        }

        /// <summary>按类型创建一条带合理默认值的端点。</summary>
        public static CommEndpointConfig Create(string name, string kind)
        {
            CommEndpointConfig c = new CommEndpointConfig();
            c.Name = name ?? "";
            c.Kind = string.IsNullOrWhiteSpace(kind) ? CommKind.TcpClient : kind.Trim();
            c.Port = CommKind.DefaultPort(c.Kind);
            c.Host = c.Kind == CommKind.TcpServer ? "" : "127.0.0.1";
            return c;
        }

        /// <summary>
        /// 归一化：DataContractJsonSerializer <strong>不调用构造函数</strong>，
        /// 旧配置缺失的字段反序列化出来是 null / 0 而不是构造里的默认值，
        /// 故所有读取处都要兜底（铁律 4）。
        /// </summary>
        public void Normalize()
        {
            Name = (Name ?? "").Trim();
            // 空值才回落 TCP；未知协议保留（由 Communication\*.dll 插件提供），
            // 否则 OPC UA 等插件端点会被静默改成 TCP 客户端。
            if (string.IsNullOrWhiteSpace(Kind)) Kind = CommKind.TcpClient;
            Host = (Host ?? "").Trim();
            if (Port <= 0 || Port > 65535) Port = CommKind.DefaultPort(Kind);
            if (Kind == CommKind.TcpServer) Host = "";   // 服务端只监听本地端口，不需要目标主机
            if (SlaveId < MinSlaveId || SlaveId > MaxSlaveId) SlaveId = DefaultSlaveId;
            if (string.IsNullOrWhiteSpace(Framing)) Framing = "None";
            if (TimeoutMs <= 0) TimeoutMs = 1000;
            if (ReconnectMs < 0) ReconnectMs = 0;
        }

        /// <summary>界面「目标」列文本：客户端 host:port，服务端 "监听 :port"。</summary>
        public string TargetText
        {
            get
            {
                if (Kind == CommKind.TcpServer) return "监听 :" + Port;
                return (Host.Length == 0 ? "?" : Host) + ":" + Port;
            }
        }

        /// <summary>界面「类型」列文本（中文）。</summary>
        public string KindDisplay
        {
            get { return CommKind.IsBuiltIn(Kind) ? CommKind.Display(Kind) : (Kind ?? ""); }
        }

        public CommEndpointConfig Clone()
        {
            return new CommEndpointConfig
            {
                Name = Name,
                Kind = Kind,
                Host = Host,
                Port = Port,
                SlaveId = SlaveId,
                Enabled = Enabled,
                AutoConnect = AutoConnect,
                Framing = Framing,
                TimeoutMs = TimeoutMs,
                ReconnectMs = ReconnectMs
            };
        }
    }

    /// <summary>
    /// 一个点位：协议无关的名称 → 地址映射。
    /// <para>流程/任务只写点位名，不关心是 Modbus 寄存器还是 OPC UA NodeId。</para>
    /// </summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class CommPointConfig
    {
        [System.Runtime.Serialization.DataMember] public string Name { get; set; }
        [System.Runtime.Serialization.DataMember] public string Endpoint { get; set; }
        [System.Runtime.Serialization.DataMember] public string Address { get; set; }

        /// <summary>Read / Write / ReadWrite。</summary>
        [System.Runtime.Serialization.DataMember] public string Direction { get; set; }

        /// <summary>Bool / Int16 / UInt16 / Int32 / UInt32 / Float / Double / String。</summary>
        [System.Runtime.Serialization.DataMember] public string Type { get; set; }

        /// <summary>寄存器个数 / 字符串长度；1 = 单值。</summary>
        [System.Runtime.Serialization.DataMember] public int Length { get; set; }

        /// <summary>Modbus 多寄存器字序：ABCD / CDAB / BADC / DCBA。</summary>
        [System.Runtime.Serialization.DataMember] public string WordOrder { get; set; }

        [System.Runtime.Serialization.DataMember] public double Scale { get; set; }
        [System.Runtime.Serialization.DataMember] public double Offset { get; set; }
        [System.Runtime.Serialization.DataMember] public string Default { get; set; }
        [System.Runtime.Serialization.DataMember] public string Description { get; set; }

        public CommPointConfig()
        {
            Name = "";
            Endpoint = "";
            Address = "";
            Direction = "ReadWrite";
            Type = "Bool";
            Length = 1;
            WordOrder = "ABCD";
            Scale = 1.0;
            Offset = 0.0;
            Default = "";
            Description = "";
        }

        public void Normalize()
        {
            Name = (Name ?? "").Trim();
            Endpoint = (Endpoint ?? "").Trim();
            Address = (Address ?? "").Trim();
            Direction = NormalizeDirection(Direction);
            Type = NormalizeType(Type);
            if (Length <= 0) Length = 1;
            if (Length > 125) Length = 125;
            WordOrder = string.IsNullOrWhiteSpace(WordOrder) ? "ABCD" : WordOrder.Trim().ToUpperInvariant();
            if (Scale == 0) Scale = 1.0;
            Default = Default ?? "";
            Description = Description ?? "";
        }

        public static string NormalizeDirection(string value)
        {
            if (string.Equals(value, "Read", StringComparison.OrdinalIgnoreCase)) return "Read";
            if (string.Equals(value, "Write", StringComparison.OrdinalIgnoreCase)) return "Write";
            return "ReadWrite";
        }

        public static string NormalizeType(string value)
        {
            string[] all = { "Bool", "Int16", "UInt16", "Int32", "UInt32", "Float", "Double", "String" };
            foreach (string t in all)
                if (string.Equals(value, t, StringComparison.OrdinalIgnoreCase)) return t;

            // 【别名，别删】Modbus 圈里 Float32 / Float64 是通用叫法，这里存的是 Float / Double。
            // 不认就会**静默回落 Bool**：写 1.5 变成写 0、写 -3 变成写 1，而且一句提示都没有
            // （手改 comm.json 或导入别人的工程时必踩，现场已经踩过一次）。
            string v = (value ?? "").Trim();
            if (string.Equals(v, "Float32", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Single", StringComparison.OrdinalIgnoreCase)
                || string.Equals(v, "Real", StringComparison.OrdinalIgnoreCase)) return "Float";
            if (string.Equals(v, "Float64", StringComparison.OrdinalIgnoreCase)) return "Double";

            return "Bool";
        }

        public CommPointConfig Clone()
        {
            return new CommPointConfig
            {
                Name = Name, Endpoint = Endpoint, Address = Address,
                Direction = Direction, Type = Type, Length = Length, WordOrder = WordOrder,
                Scale = Scale, Offset = Offset, Default = Default, Description = Description
            };
        }
    }

    /// <summary>消息模板：结果 / 心跳 / 组合信息的文本或 JSON。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class CommMessageConfig
    {
        [System.Runtime.Serialization.DataMember] public string Name { get; set; }
        [System.Runtime.Serialization.DataMember] public string Endpoint { get; set; }

        /// <summary>Text / Json / Points。</summary>
        [System.Runtime.Serialization.DataMember] public string Mode { get; set; }

        /// <summary>Text/Json 时是模板；Points 时是 <c>Point=value;Point=value</c>。</summary>
        [System.Runtime.Serialization.DataMember] public string Format { get; set; }

        [System.Runtime.Serialization.DataMember] public string Terminator { get; set; }

        /// <summary>Mode=Points 时按顺序写入的点位名（可选，Format 优先）。</summary>
        [System.Runtime.Serialization.DataMember] public List<string> Points { get; set; }

        public CommMessageConfig()
        {
            Name = "";
            Endpoint = "";
            Mode = "Text";
            Format = "";
            Terminator = "\r\n";
            Points = new List<string>();
        }

        public void Normalize()
        {
            Name = (Name ?? "").Trim();
            Endpoint = (Endpoint ?? "").Trim();
            if (string.IsNullOrWhiteSpace(Mode)) Mode = "Text";
            Format = Format ?? "";
            Terminator = Terminator ?? "";
            if (Points == null) Points = new List<string>();
        }

        public CommMessageConfig Clone()
        {
            var c = new CommMessageConfig { Name = Name, Endpoint = Endpoint, Mode = Mode, Format = Format, Terminator = Terminator };
            if (Points != null) c.Points.AddRange(Points);
            return c;
        }
    }

    /// <summary>任务动作。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class CommActionConfig
    {
        /// <summary>SetPoint / SendMessage / WaitPoint / Sleep / StartFlow / Log。</summary>
        [System.Runtime.Serialization.DataMember] public string Kind { get; set; }
        [System.Runtime.Serialization.DataMember] public string Target { get; set; }
        [System.Runtime.Serialization.DataMember] public string Value { get; set; }
        [System.Runtime.Serialization.DataMember] public int TimeoutMs { get; set; }
        [System.Runtime.Serialization.DataMember] public bool ContinueOnError { get; set; }

        public CommActionConfig()
        {
            Kind = "SetPoint";
            Target = "";
            Value = "";
            TimeoutMs = 1000;
        }

        public void Normalize()
        {
            Kind = string.IsNullOrWhiteSpace(Kind) ? "SetPoint" : Kind.Trim();
            Target = (Target ?? "").Trim();
            Value = Value ?? "";
            if (TimeoutMs <= 0) TimeoutMs = 1000;
        }

        public CommActionConfig Clone()
        {
            return new CommActionConfig { Kind = Kind, Target = Target, Value = Value, TimeoutMs = TimeoutMs, ContinueOnError = ContinueOnError };
        }
    }

    /// <summary>常驻通讯任务。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class CommJobConfig
    {
        [System.Runtime.Serialization.DataMember] public string Name { get; set; }
        [System.Runtime.Serialization.DataMember] public bool Enabled { get; set; }

        /// <summary>Timer / ResultGroup / PointEdge / Startup。</summary>
        [System.Runtime.Serialization.DataMember] public string Trigger { get; set; }

        [System.Runtime.Serialization.DataMember] public int IntervalMs { get; set; }

        /// <summary>流程 Id 或名称（ResultGroup / StartFlow 用）。</summary>
        [System.Runtime.Serialization.DataMember] public List<string> Flows { get; set; }

        /// <summary>Latest / AllFresh。</summary>
        [System.Runtime.Serialization.DataMember] public string Mode { get; set; }

        [System.Runtime.Serialization.DataMember] public int WindowMs { get; set; }

        /// <summary>PointEdge 的触发点位。</summary>
        [System.Runtime.Serialization.DataMember] public string Point { get; set; }

        /// <summary>
        /// 多流程触发步长（0 = 关闭，老行为：只读 <see cref="Point"/> 一个点位）。
        /// <para>大于 0 时按**流程在「流程管理」里的顺序**展开：第 i 条流程读
        /// <c>Point 的地址 + i × FlowStride</c>，一条任务即可驱动多条流程。
        /// 与 SendData 的 <c>MbFlowStride</c> 用同一个值，触发与结果才落在同一分区。</para>
        /// </summary>
        [System.Runtime.Serialization.DataMember] public int FlowStride { get; set; }
        [System.Runtime.Serialization.DataMember] public string Expect { get; set; }

        [System.Runtime.Serialization.DataMember] public List<CommActionConfig> Actions { get; set; }

        public CommJobConfig()
        {
            Name = "";
            Enabled = true;
            Trigger = "Timer";
            IntervalMs = 1000;
            Flows = new List<string>();
            Mode = "Latest";
            WindowMs = 500;
            Point = "";
            FlowStride = 0;
            Expect = "true";
            Actions = new List<CommActionConfig>();
        }

        public void Normalize()
        {
            Name = (Name ?? "").Trim();
            Trigger = string.IsNullOrWhiteSpace(Trigger) ? "Timer" : Trigger.Trim();
            if (IntervalMs <= 0) IntervalMs = 1000;
            if (Flows == null) Flows = new List<string>();
            Mode = string.Equals(Mode, "AllFresh", StringComparison.OrdinalIgnoreCase) ? "AllFresh" : "Latest";
            if (WindowMs <= 0) WindowMs = 500;
            Point = (Point ?? "").Trim();
            if (FlowStride < 0) FlowStride = 0;
            if (FlowStride > 65535) FlowStride = 65535;
            Expect = Expect ?? "true";
            if (Actions == null) Actions = new List<CommActionConfig>();
            foreach (CommActionConfig a in Actions) if (a != null) a.Normalize();
        }

        public CommJobConfig Clone()
        {
            var c = new CommJobConfig
            {
                Name = Name, Enabled = Enabled, Trigger = Trigger, IntervalMs = IntervalMs,
                Mode = Mode, WindowMs = WindowMs, Point = Point,
                FlowStride = FlowStride, Expect = Expect
            };
            if (Flows != null) c.Flows.AddRange(Flows);
            if (Actions != null) foreach (CommActionConfig a in Actions) if (a != null) c.Actions.Add(a.Clone());
            return c;
        }
    }

    /// <summary>把某条流程的 Busy / Ready / Fault 映射到点位（多相机多流程时一条流程一份）。</summary>
    [System.Runtime.Serialization.DataContract]
    public sealed class CommFlowBindingConfig
    {
        /// <summary>流程 Id（优先）或流程名。</summary>
        [System.Runtime.Serialization.DataMember] public string Flow { get; set; }

        [System.Runtime.Serialization.DataMember] public string Endpoint { get; set; }
        [System.Runtime.Serialization.DataMember] public string BusyPoint { get; set; }
        [System.Runtime.Serialization.DataMember] public string ReadyPoint { get; set; }
        [System.Runtime.Serialization.DataMember] public string FaultPoint { get; set; }
        [System.Runtime.Serialization.DataMember] public bool Enabled { get; set; }

        public CommFlowBindingConfig()
        {
            Flow = "";
            Endpoint = "";
            BusyPoint = "";
            ReadyPoint = "";
            FaultPoint = "";
            Enabled = true;
        }

        public void Normalize()
        {
            Flow = (Flow ?? "").Trim();
            Endpoint = (Endpoint ?? "").Trim();
            BusyPoint = (BusyPoint ?? "").Trim();
            ReadyPoint = (ReadyPoint ?? "").Trim();
            FaultPoint = (FaultPoint ?? "").Trim();
        }

        public CommFlowBindingConfig Clone()
        {
            return new CommFlowBindingConfig
            {
                Flow = Flow, Endpoint = Endpoint, BusyPoint = BusyPoint,
                ReadyPoint = ReadyPoint, FaultPoint = FaultPoint, Enabled = Enabled
            };
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

            // 配置在 <程序目录>\Config\ 下：先建目录并做旧位置迁移，
            // 保证第一次读就能拿到老配置（否则会静默用默认值，用户以为配置丢了）。
            AppPaths.Ensure();

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

            // 新增的 UI 偏好：老配置里没有这两个字段 → 反序列化得 0 → 归一化补默认值。
            // 【必须是"归一化"而不是"钳制"】高度钳到 120 会让老用户一升级就发现"结果区变矮"；
            // 端子区若把缺失当 false（展开）就与"默认收起"的需求相反（详见 UiSettings 注释）。
            cfg.Ui.FlowResultPanelHeight = UiSettings.NormalizeResultPanelHeight(cfg.Ui.FlowResultPanelHeight);
            cfg.Ui.FlowPortsState = UiSettings.NormalizePortsState(cfg.Ui.FlowPortsState);
            // 标注详细度：老配置没有该字段 → 反序列化得 0 → 归一化为"标准"
            cfg.Ui.FlowLabelDetail = UiSettings.NormalizeLabelDetail(cfg.Ui.FlowLabelDetail);
            // 右栏宽度：老配置没有该字段 → 反序列化得 0 → 补默认 400
            cfg.Ui.FlowRightWidth = UiSettings.NormalizeRightWidth(cfg.Ui.FlowRightWidth);
            if (cfg.Flows == null) cfg.Flows = new FlowSettings();
            if (cfg.Flows.Flows == null) cfg.Flows.Flows = new List<FlowDefinition>();
            if (cfg.Comm == null) cfg.Comm = CommSettings.CreateDefault();
            if (cfg.Comm.Endpoints == null) cfg.Comm.Endpoints = new List<CommEndpointConfig>();

            // 【旧配置兼容，请勿移除】标定段是后加的：旧版 appconfig.json 里根本没有
            // Calibrations，反序列化得 null；段存在但缺字段时 Records / CurrentByCamera 也是 null。
            // 这里统一归一化（补空容器 + 逐条记录补 Affine + 清理指向已删记录的「当前」映射），
            // 否则标定窗体一打开就会在 RecordsFor / CurrentByCamera 上 NRE。
            if (cfg.Calibrations == null) cfg.Calibrations = CalibSettings.CreateDefault();
            CalibSettings.Normalize(cfg.Calibrations);

            // 【旧配置兼容，请勿移除】通讯段是后加的：旧版 appconfig.json 里根本没有 Comm，
            // 反序列化得 null → 上面已补默认；手改过的配置若缺 Enabled 字段，bool 会给 false，
            // 表现为「通讯建好了却一条都连不上」，故走一次显式升级。
            if (CommSettings.UpgradeToCurrent(cfg.Comm))
            {
                Logger.Info("通讯配置已就地升级到 Schema v" + CommSettings.CurrentSchemaVersion
                            + "（补齐 Enabled 等新字段默认值）");
            }

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

            // 【旧配置兼容，请勿移除】旧版 appconfig.json 没有 RunMode 字段，反序列化后为 null。
            // RunMode 刻意用字符串而非 bool：缺失 bool 会变 false，无法区分"没配过"与"显式选连续"，
            // 会让所有老配置静默退回连续运行 —— 正是"不触发也在跑"的病灶。缺失即 null → 触发式。
            cfg.System.RunMode = SystemSettings.NormalizeRunMode(cfg.System.RunMode);

            // 【旧配置兼容，请勿移除】旧版 appconfig.json 没有 DbPath / LogRetentionDays 字段，
            // 反序列化后 DbPath 为 null、LogRetentionDays 为 0，这里归一化为默认值
            // （0 视为"未设置保留天数"，回落到默认 90，而不是"不清理"）。
            if (cfg.System.DbPath == null) cfg.System.DbPath = "";
            if (cfg.System.LogRetentionDays <= 0) cfg.System.LogRetentionDays = SystemSettings.DefaultLogRetentionDays;

            // 【旧配置兼容，请勿移除】旧版 appconfig.json 没有这三项：
            // - CurrentProduct 缺失即 null → 归一化为 ""（= 全部）
            // - MaxParallelFlows 缺失即 0 → 补默认值 4（不能 Clamp，否则 0→1 等于关掉并行）
            // - AllowCameraShare 缺失即 false → 正是默认值（严格独占），无需额外处理
            if (cfg.Ui.CurrentProduct == null) cfg.Ui.CurrentProduct = "";
            cfg.Ui.MaxParallelFlows = UiSettings.NormalizeMaxParallelFlows(cfg.Ui.MaxParallelFlows);
            cfg.Ui.MaxFlowFps = UiSettings.NormalizeMaxFlowFps(cfg.Ui.MaxFlowFps);

            // 【旧配置兼容，请勿移除】旧版 appconfig.json 没有 Language 字段，
            // 反序列化后为 null，这里归一化为默认语言 zh-CN。
            // 刻意不"钳制"未知代码（如手改出的 "jp"）：语言代码是开放集合，
            // 钳成 zh-CN 会把用户手改的值静默抹掉，交由 Lang.Normalize 在读取端兜底即可。
            if (string.IsNullOrWhiteSpace(cfg.Ui.Language)) cfg.Ui.Language = Lang.ZhCN;

            // 【旧配置兼容，请勿移除】旧版 appconfig.json 没有 Theme 字段，
            // 反序列化后为 null，这里补默认深色，保证老用户升级后界面观感不变。
            if (string.IsNullOrWhiteSpace(cfg.Ui.Theme)) cfg.Ui.Theme = UiSettings.DefaultTheme;

            // 方案级数据（流程 / 通讯 / 标定）的真源是 Recipes\<方案>\，不在本文件里。
            // 旧版本把它们写在 appconfig 里 —— 这里做一次性迁移，之后一律从方案目录装载。
            CurrentRecipeBootstrap(cfg);

            _current = cfg;
            return cfg;
        }

        /// <summary>
        /// 按 <see cref="AlgorithmSettings.CurrentRecipeName"/> 装载方案级数据。
        /// <para>
        /// 两种情形：
        /// ① <b>迁移</b> —— appconfig 里还有流程数据（旧版本写的）：以它为准写进方案目录。
        ///    迁移只会在文件还没被新版 Save 覆写过时发生，因此天然只执行一次。
        /// ② <b>正常装载</b> —— 从 <c>Recipes\&lt;方案&gt;\</c> 读 flows / comm / calibrations 到内存。
        /// </para>
        /// <para>
        /// 【禁止在本方法内访问 ConfigStore.Current】Load 正在执行，getter 会再触发一次 Load（递归）。
        /// 需要配置就只用传进来的 <paramref name="cfg"/>。
        /// </para>
        /// </summary>
        private static void CurrentRecipeBootstrap(AppConfig cfg)
        {
            string recipe = (cfg.Algorithm != null && cfg.Algorithm.CurrentRecipeName != null)
                ? cfg.Algorithm.CurrentRecipeName.Trim() : "";
            if (recipe.Length == 0) return;      // 还没有方案：保持现状（首次运行会走默认流程）

            bool appHasFlows = cfg.Flows != null && cfg.Flows.Flows != null && cfg.Flows.Flows.Count > 0;

            if (appHasFlows)
            {
                // ① 迁移：旧版本把方案数据写在 appconfig 里，先备份再写进方案目录
                try
                {
                    string bak = AppPaths.ConfigFile + ".before-split.bak";
                    if (!File.Exists(bak) && File.Exists(AppPaths.ConfigFile))
                    {
                        File.Copy(AppPaths.ConfigFile, bak, true);
                        Logger.Info("已备份拆分前的全局配置：" + bak);
                    }

                    OpResult w = ProjectBundle.WriteProjectFiles(recipe, cfg);
                    if (w.Success)
                        Logger.Info("已把流程/通讯/标定迁入方案目录：" + recipe
                                    + "（此后全局配置不再保存这些数据）");
                    else
                        Logger.Warn("迁移方案目录失败：" + w.Message);
                }
                catch (Exception ex)
                {
                    Logger.Warn("迁移方案目录异常：" + ex.Message);
                }
                return;      // 内存里的 cfg 就是最新，无需再读盘
            }

            // ② 正常装载
            string dir = AppPaths.RecipePath(recipe);
            try
            {
                string flowsPath = Path.Combine(dir, ProjectBundle.FlowsFile);
                if (File.Exists(flowsPath))
                {
                    FlowSettings fs = Json.LoadFromFile<FlowSettings>(flowsPath);
                    if (fs != null)
                    {
                        FlowSettings.UpgradeToCurrent(fs);
                        if (fs.Flows != null && fs.Flows.Count > 0)
                        {
                            cfg.Flows = fs;
                            if (string.IsNullOrEmpty(cfg.Flows.ActiveFlowId)
                                || cfg.Flows.Flows.Find(x => x != null && x.Id == cfg.Flows.ActiveFlowId) == null)
                            {
                                cfg.Flows.ActiveFlowId = cfg.Flows.Flows[0].Id;
                            }
                        }
                    }
                }

                string commPath = Path.Combine(dir, ProjectBundle.CommFile);
                if (File.Exists(commPath))
                {
                    CommSettings cs = Json.LoadFromFile<CommSettings>(commPath);
                    if (cs != null && cs.Endpoints != null && cs.Endpoints.Count > 0)
                    {
                        CommSettings.UpgradeToCurrent(cs);
                        cfg.Comm = cs;
                    }
                }

                string calibPath = Path.Combine(dir, ProjectBundle.CalibFile);
                if (File.Exists(calibPath))
                {
                    CalibSettings cal = Json.LoadFromFile<CalibSettings>(calibPath);
                    if (cal != null)
                    {
                        CalibSettings.Normalize(cal);
                        cfg.Calibrations = cal;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("从方案目录装载失败（回落到默认）：" + ex.Message);
            }
        }

        /// <summary>保存配置并广播变更。</summary>
        public static void Save(AppConfig config)
        {
            if (config == null) return;

            _current = config;

            try
            {
                // 配置目录可能还没建（Ensure 未跑到 / 用户手工删过）—— 保存前兜底
                if (!Directory.Exists(AppPaths.ConfigDir)) Directory.CreateDirectory(AppPaths.ConfigDir);

                // 【方案级数据不写进全局配置】流程 / 通讯 / 标定都在 Recipes\<方案>\ 下，
                // 这里是"本机全局参数"（System / Algorithm / Ui）。两份都存必然不同步 ——
                // 现场"改了参数、重启又在、可方案包还是旧的"就是这么来的。
                AppConfig disk = new AppConfig
                {
                    System = config.System,
                    Algorithm = config.Algorithm,
                    Ui = config.Ui,
                    Flows = null,
                    Comm = null,
                    Calibrations = null
                };
                Json.SaveToFile(AppPaths.ConfigFile, disk);
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
