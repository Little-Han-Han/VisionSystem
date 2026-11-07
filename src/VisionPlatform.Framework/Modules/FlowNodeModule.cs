using System;
using System.Collections.Generic;
using System.Drawing;
using VisionPlatform.Flow;

namespace VisionPlatform.Framework.Modules
{
    /// <summary>
    /// 画布「参数推导布局预览」的类型。
    /// <para>
    /// 只描述<b>画什么形状</b>，具体参数键由宿主按 <see cref="LayoutPreviewSpec.Prefix"/> 约定读取：
    /// <c>{Prefix}X1/Y1/X2/Y2/CaliperCount/ProjectionLength/FilterHalfWidth</c>（直线类）或
    /// <c>{Prefix}Cx/Cy/InnerRadius/OuterRadius/StartAngle/AngleSpan/...</c>（环形类）。
    /// 模块用声明代替代码，宿主不需要认识具体节点类型。
    /// </para>
    /// </summary>
    public enum LayoutPreviewKind
    {
        /// <summary>不画。</summary>
        None,

        /// <summary>沿线布置的卡尺（直线卡尺 / 边缘对）。</summary>
        LineCaliper,

        /// <summary>沿圆周布置的卡尺（圆卡尺）。</summary>
        CircleCaliper
    }

    /// <summary>一条布局预览声明。</summary>
    public sealed class LayoutPreviewSpec
    {
        public LayoutPreviewKind Kind { get; set; }

        /// <summary>参数键前缀（空 = 直接 X1/Y1/...；<c>Line1</c> = Line1X1/Line1Y1/...）。</summary>
        public string Prefix { get; set; }

        /// <summary>预览上方的提示文案（空 = 宿主用默认文案）。</summary>
        public string Note { get; set; }

        public LayoutPreviewSpec()
        {
            Kind = LayoutPreviewKind.None;
            Prefix = "";
            Note = "";
        }

        public LayoutPreviewSpec(LayoutPreviewKind kind, string prefix, string note)
        {
            Kind = kind;
            Prefix = prefix ?? "";
            Note = note ?? "";
        }
    }

    /// <summary>
    /// 节点自带的一次性 UI 扩展点（横幅卡片 / 模板算法）。
    /// 宿主按这些<b>声明</b>决定显示哪种卡片，不需要按节点类型写 if/else。
    /// </summary>
    public sealed class NodeUiHints
    {
        /// <summary>画布节点左侧类型色条（ARGB）。</summary>
        public Color StripeColor { get; set; }

        /// <summary>参数区顶部横幅种类：空 / <c>template</c> / <c>ocr</c>。</summary>
        public string BannerKind { get; set; }

        /// <summary>模板算法键（如 <c>ShapeMatch</c>）；非匹配类为空。</summary>
        public string TemplateAlgorithm { get; set; }

        /// <summary>布局预览声明（可多条，如交点卡尺两组扫描线）。</summary>
        public List<LayoutPreviewSpec> Previews { get; private set; }

        public NodeUiHints()
        {
            StripeColor = Color.FromArgb(160, 170, 190);
            BannerKind = "";
            TemplateAlgorithm = "";
            Previews = new List<LayoutPreviewSpec>();
        }
    }

    /// <summary>
    /// 一个算法模块的<b>完整声明</b>：身份、分类、端子、默认参数、参数表、界面提示。
    /// <para>
    /// 这是「算法 / 界面 / 主框架」三者之间唯一的契约：
    /// <list type="bullet">
    /// <item>模块包（<c>VisionPlatform.Modules.dll</c> 或第三方 DLL）只实现本接口，
    /// 不引用宿主窗体、执行器与原生算法；</item>
    /// <item>宿主只通过 <see cref="FlowModuleRegistry"/> 取模块，不再按节点类型写 switch；</item>
    /// <item>界面的参数面板由 <see cref="CreateParams"/> 的声明式表驱动，无需为每个节点写面板。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 【扩展方式】新增算法 = 新增一个 <see cref="IFlowNodeModule"/> 实现并注册；
    /// 宿主的工具箱、画布、参数面板、校验器、结果区全部自动出现，无需改动界面代码。
    /// 执行语义仍由宿主的执行器负责（本期不改变算法执行代码）。
    /// </para>
    /// </summary>
    public interface IFlowNodeModule
    {
        /// <summary>稳定标识（= <c>FlowNode.Type</c> 持久化的字符串，发布后不得改名）。</summary>
        string Key { get; }

        /// <summary>工具箱 / 节点默认名。</summary>
        string DisplayName { get; }

        /// <summary>分类 Key，见 <see cref="ModuleCategories"/>。</summary>
        string CategoryKey { get; }

        /// <summary>工具箱排序（越小越靠上；相同值保持注册顺序）。</summary>
        int Order { get; }

        /// <summary>一句话说明（工具提示用，可空）。</summary>
        string Description { get; }

        /// <summary>输入端子。</summary>
        List<FlowPortDef> Inputs { get; }

        /// <summary>输出端子（第 1 项为主输出）。</summary>
        List<FlowPortDef> Outputs { get; }

        /// <summary>新节点的默认参数。</summary>
        List<ParamItem> CreateDefaults();

        /// <summary>声明式参数表（界面按此生成，不写代码）。</summary>
        NodeParamTable CreateParams();

        /// <summary>界面提示（色条 / 横幅 / 布局预览）。</summary>
        NodeUiHints Ui { get; }

        /// <summary>
        /// 仅「新建节点」时覆盖的默认值（与 <see cref="CreateDefaults"/> 分开，
        /// 避免老配置升级时被新默认值影响）。
        /// </summary>
        Dictionary<string, string> CreateNewNodeOverrides();
    }

    /// <summary>
    /// 模块包入口：一个 DLL 可以实现多个包，启动时由 <see cref="ModuleLoader"/> 反射创建并调用。
    /// <para>实现类必须有公共无参构造函数。</para>
    /// </summary>
    public interface IFlowModulePackage
    {
        /// <summary>包名（日志用）。</summary>
        string Name { get; }

        /// <summary>把本包内的模块注册到 <see cref="FlowModuleRegistry"/>。</summary>
        void Register();
    }
}
