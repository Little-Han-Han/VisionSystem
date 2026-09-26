using System;
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
