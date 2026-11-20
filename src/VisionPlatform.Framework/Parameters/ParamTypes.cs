using System;
using System.Collections.Generic;

namespace VisionPlatform.Flow
{
    /// <summary>参数控件类型。</summary>
    public enum NodeParamKind
    {
        /// <summary>整数（NumericUpDown，Decimals=0）。</summary>
        Int,

        /// <summary>浮点数（NumericUpDown）。</summary>
        Double,

        /// <summary>布尔（CheckBox，存 1 / 0）。</summary>
        Bool,

        /// <summary>自由文本（TextBox）。</summary>
        Text,

        /// <summary>代码/多行文本（等宽字体多行 TextBox，高度较大）。</summary>
        Code,

        /// <summary>枚举（ComboBox 下拉，存选项的 Value）。</summary>
        Enum,

        /// <summary>文件路径（TextBox + 「浏览」按钮）。</summary>
        File,

        /// <summary>
        /// 动作行（一个按钮，<strong>不对应参数值</strong>）：点击由
        /// <c>NodeParamPanel.ActionInvoked</c> 把 <see cref="NodeParamField.Key"/> 抛给调用方。
        /// </summary>
        Action,

        /// <summary>分组标题（不对应参数）。</summary>
        Group,

        /// <summary>只读提示文本（不对应参数）。</summary>
        Note,

        /// <summary>
        /// 目录选择行（TextBox + 「浏览…」+「打开」）。
        /// <strong>末尾追加</strong>：枚举值向前兼容，已有序列化值不会错位。
        /// </summary>
        Folder
    }

    /// <summary>枚举选项：显示为 <see cref="NodeParamOption.Text"/>，落盘为 <see cref="NodeParamOption.Value"/>。</summary>
    public sealed class NodeParamOption
    {
        /// <summary>界面显示文本。</summary>
        public string Text { get; set; }

        /// <summary>实际存入 <see cref="ParamItem.Value"/> 的字符串。</summary>
        public string Value { get; set; }

        public NodeParamOption() { Text = ""; Value = ""; }

        public NodeParamOption(string text, string value)
        {
            Text = text;
            Value = value;
        }

        public override string ToString() { return Text; }
    }

    /// <summary>单个参数字段的声明。</summary>
    public sealed class NodeParamField
    {
        /// <summary>参数键（与 <see cref="FlowModuleInfo.CreateDefaultParams"/> 里的 Key 严格一致）。</summary>
        public string Key { get; set; }

        /// <summary>标签文本。</summary>
        public string Label { get; set; }

        /// <summary>控件类型。</summary>
        public NodeParamKind Kind { get; set; }

        /// <summary>默认值（字符串形态，InvariantCulture）。</summary>
        public string Default { get; set; }

        /// <summary>最小值（数值型）。</summary>
        public decimal Min { get; set; }

        /// <summary>最大值（数值型）。</summary>
        public decimal Max { get; set; }

        /// <summary>小数位数（0 = 整数）。</summary>
        public int Decimals { get; set; }

        /// <summary>步进。</summary>
        public decimal Increment { get; set; }

        /// <summary>控件高度（仅 Code 这类多行控件用；0 = 默认单行高）。</summary>
        public int Height { get; set; }

        /// <summary>枚举选项（Kind=Enum 时有效）。</summary>
        public List<NodeParamOption> Options { get; set; }

        /// <summary>提示文本（可空）。</summary>
        public string Hint { get; set; }

        /// <summary>
        /// 动态选项来源 Key（空 = 使用静态 <see cref="Options"/>）。
        /// <para>
        /// 由<b>宿主</b>在渲染参数面板时解析并填充（如 <c>comm</c> = 通讯列表、
        /// <c>calib:NPoint</c> = N 点标定记录）。模块只声明"要哪一类选项"，
        /// 不直接依赖通讯 / 标定等业务服务 —— 这是参数表能脱离宿主复用的关键。
        /// </para>
        /// </summary>
        public string OptionsSource { get; set; }

        /// <summary>
        /// 运行时实时值来源 Key（空 = 普通参数）。
        /// <para>例如 <c>coord</c>：该数值框在运行期显示上游连线传入的实时坐标，并置为只读。</para>
        /// </summary>
        public string LiveSource { get; set; }

        /// <summary>
        /// 是否为「不常用」参数：true 时 <c>NodeParamPanel</c> 把它收进默认折叠的
        /// 「高级参数」区，避免参数多的节点（形状匹配 / 模板匹配 / Blob / 卡尺）一打开就是一长条。
        /// <para>它<strong>只影响界面归组</strong>，与 Key、默认值、取值范围、落盘完全无关，
        /// 折叠状态下参数照常读写。</para>
        /// </summary>
        public bool Advanced { get; set; }

        /// <summary>
        /// 禁用原因（空 / null = 可用）。目前只对 <see cref="NodeParamKind.Action"/> 有意义：
        /// 非空时按钮置灰不可点，tooltip 显示这段原因而不是 <see cref="Hint"/>。
        /// <para>
        /// 为什么要有它而不是"干脆不加这个 Action 行"：功能尚未实现时，
        /// <strong>看得见但明显不可用</strong>远好于"看得见、点得动、点了没反应"——
        /// 后者会被当成 bug 反馈，而且会和"上一版真的坏过的那个按钮"混在一起。
        /// </para>
        /// </summary>
        public string DisabledReason { get; set; }

        /// <summary>
        /// 文件选择对话框的过滤器（仅 <see cref="NodeParamKind.File"/>；空 = 按 Key 猜）。
        /// <para>
        /// 【为什么要显式声明】以前是靠 Key 里是否含 "file"/"path"/"image"/"model" 来猜过滤器，
        /// 于是「字符库文件」(.ocrtpl) 会被当成图片过滤器，用户根本选不到自己的库。
        /// 参数表知道自己的文件是什么类型，就该由它直接说。
        /// </para>
        /// </summary>
        public string FileFilter { get; set; }

        /// <summary>
        /// 文件扩展名（不含点；仅 <see cref="NodeParamKind.File"/>）。浏览后若路径缺少该后缀会自动补上，
        /// 避免用户存出 <c>line1</c> 这种没有扩展名、下次又找不到的文件。
        /// </summary>
        public string FileExt { get; set; }

        /// <summary>
        /// 允许选择不存在的文件（仅 <see cref="NodeParamKind.File"/>）。
        /// <para>库 / 配置这类"先定名字、保存时才创建"的文件必须为 true，否则用户无法新建。</para>
        /// </summary>
        public bool FileAllowCreate { get; set; }

        /// <summary>是否禁用（<see cref="DisabledReason"/> 非空即禁用）。</summary>
        public bool IsDisabled
        {
            get { return !string.IsNullOrEmpty(DisabledReason); }
        }

        public NodeParamField()
        {
            Key = "";
            Label = "";
            Kind = NodeParamKind.Int;
            Default = "0";
            Min = 0m;
            Max = 100000m;
            Decimals = 0;
            Increment = 1m;
            Options = new List<NodeParamOption>();
            Hint = "";
            DisabledReason = "";
            OptionsSource = "";
            LiveSource = "";
        }

        /// <summary>是否为「不对应参数」的展示行（分组标题 / 提示文本）。</summary>
        public bool IsDecorative
        {
            get { return Kind == NodeParamKind.Group || Kind == NodeParamKind.Note; }
        }
    }

    /// <summary>一个节点类型的完整参数表。</summary>
    public sealed class NodeParamTable
    {
        /// <summary>节点类型。</summary>
        public FlowNodeType Type { get; set; }

        /// <summary>面板标题（一般取模块显示名）。</summary>
        public string Title { get; set; }

        /// <summary>字段列表（顺序即界面顺序）。</summary>
        public List<NodeParamField> Fields { get; set; }

        /// <summary>面板底部固定提示（如算法限制说明）。</summary>
        public string FooterNote { get; set; }

        public NodeParamTable()
        {
            Title = "";
            Fields = new List<NodeParamField>();
            FooterNote = "";
        }
    }
}
