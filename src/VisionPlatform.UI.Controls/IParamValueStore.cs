using System;
using System.Collections.Generic;
using VisionPlatform.Flow;

namespace VisionPlatform.UI
{
    /// <summary>
    /// 参数值的读写抽象：让 <see cref="ParamListControl"/> 不依赖任何具体节点 / 配置对象。
    /// <para>宿主用 <c>FlowNode</c> 实现它；其它场景（配方、测试）可以用字典实现，控件完全复用。</para>
    /// </summary>
    public interface IParamValueStore
    {
        /// <summary>该键是否存在（决定是否回落到字段默认值）。</summary>
        bool Has(string key);

        /// <summary>读取原始字符串值。</summary>
        string Get(string key);

        void SetInt(string key, int value);
        void SetDouble(string key, double value);
        void SetBool(string key, bool value);
        void SetText(string key, string value);

        /// <summary>
        /// 运行时实时值（如上游连线传入的坐标）。
        /// <para>返回 false = 没有实时值，控件显示参数里存的值。</para>
        /// </summary>
        bool TryGetLive(string source, out string value, out bool readOnly, out string tooltip);
    }

    /// <summary>
    /// 动态枚举选项提供者：参数表只声明 <see cref="NodeParamField.OptionsSource"/>，
    /// 由宿主按来源填充（通讯列表 / 标定记录 / 配方…）。
    /// </summary>
    public interface IParamOptionProvider
    {
        /// <summary>按字段的 OptionsSource 返回选项；未知来源返回 null（控件回落到静态 Options）。</summary>
        List<NodeParamOption> GetOptions(NodeParamField field);
    }
}
