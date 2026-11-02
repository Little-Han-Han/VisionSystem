using System;
using System.Collections.Generic;
using VisionPlatform.Vision;

namespace VisionPlatform.Recipe
{
    /// <summary>
    /// 配方：一组完整的生产参数（相机、检测算法、通讯）。
    /// 以 JSON 存放在 %AppData%\VisionPlatform\Recipes。
    /// </summary>
    public class Recipe
    {
        public string Name { get; set; }

        /// <summary>产品型号。</summary>
        public string Product { get; set; }

        public string Note { get; set; }

        public DateTime Modified { get; set; }

        /// <summary>
        /// 该方案绑定的流程 Id 列表（可空）。
        /// <para>
        /// 【为什么需要】模板/字符库/模型按"方案 → 流程 → 节点"归档，
        /// 而配方本身不含流程信息；不建立这层绑定就永远回答不了"这个方案的模型放哪"。
        /// 空列表 = 未绑定（资产退回全局目录，行为与升级前一致）。
        /// </para>
        /// </summary>
        public List<string> FlowIds { get; set; }

        /// <summary>使用的相机 Id。</summary>
        public string CameraId { get; set; }

        /// <summary>检测参数。</summary>
        public InspectionConfig Inspection { get; set; }

        /// <summary>TCP 通讯目标（host:port），可为空。</summary>
        public string TcpTarget { get; set; }

        /// <summary>Modbus 从站地址（host:port），可为空。</summary>
        public string ModbusTarget { get; set; }

        public Recipe()
        {
            Inspection = new InspectionConfig();
            FlowIds = new List<string>();
            Modified = DateTime.Now;
        }

        public static Recipe CreateDefault(string name)
        {
            return new Recipe
            {
                Name = string.IsNullOrWhiteSpace(name) ? "默认配方" : name,
                Product = "DEFAULT",
                Note = "新建配方",
                CameraId = "SIM-0",
                Inspection = new InspectionConfig(),
                Modified = DateTime.Now
            };
        }

        public override string ToString()
        {
            return Name + (string.IsNullOrEmpty(Product) ? "" : " / " + Product);
        }
    }
}
