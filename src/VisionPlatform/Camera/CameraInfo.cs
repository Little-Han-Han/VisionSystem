using System.Runtime.Serialization;

namespace VisionPlatform.Camera
{
    /// <summary>相机描述信息。</summary>
    [DataContract]
    public class CameraInfo
    {
        /// <summary>唯一标识（模拟相机为 SIM-0 之类，海康为序列号）。</summary>
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        /// <summary>厂商：Sim / Hik。</summary>
        [DataMember]
        public string Vendor { get; set; }

        [DataMember]
        public string Serial { get; set; }

        [DataMember]
        public string Model { get; set; }

        /// <summary>网口相机的 IP（海康）。</summary>
        [DataMember]
        public string Ip { get; set; }

        public override string ToString()
        {
            string id = string.IsNullOrEmpty(Id) ? "?" : Id;
            string name = string.IsNullOrEmpty(Name) ? id : Name;
            return string.Format("[{0}] {1} ({2})", Vendor ?? "?", name, Model ?? "");
        }
    }
}
