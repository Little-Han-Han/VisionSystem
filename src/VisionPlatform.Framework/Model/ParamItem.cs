using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace VisionPlatform.Flow
{
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
}
