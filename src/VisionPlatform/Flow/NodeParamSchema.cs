using System;
using System.Collections.Generic;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 参数表查询门面：真正的数据在模块包（<c>VisionPlatform.Modules.dll</c>）里，
    /// 本类只转发到 <see cref="Framework.Modules.FlowModuleRegistry"/>。
    /// <para>
    /// 【为什么保留这个类】宿主（界面 / 校验器 / 冒烟）已有大量调用点，
    /// 保留同名门面可以做到"数据搬家、调用不变"，把重构风险压到最低。
    /// 新增模块<b>不再需要改本文件</b>。
    /// </para>
    /// </summary>
    public static class NodeParamSchema
    {
        /// <summary>按节点类型取参数表；无参数表的类型返回 null。</summary>
        public static NodeParamTable For(FlowNodeType type)
        {
            Framework.Modules.IFlowNodeModule m = Framework.Modules.FlowModuleRegistry.Find(type);
            return (m != null) ? m.CreateParams() : null;
        }

        /// <summary>该类型是否有参数表（决定面板显示参数还是空态）。</summary>
        public static bool HasSchema(FlowNodeType type)
        {
            return For(type) != null;
        }

        /// <summary>取字段的默认值（找不到返回空串）。</summary>
        public static string DefaultOf(FlowNodeType type, string key)
        {
            NodeParamTable t = For(type);
            if (t == null || string.IsNullOrEmpty(key)) return "";

            foreach (NodeParamField f in t.Fields)
            {
                if (f != null && string.Equals(f.Key, key, StringComparison.Ordinal))
                    return f.Default;
            }
            return "";
        }
    }
}
