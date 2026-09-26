using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace VisionPlatform.Core
{
    /// <summary>
    /// 基于 DataContractJsonSerializer 的轻量 JSON 助手（无第三方依赖）。
    /// 注意：被序列化的类型需要标记 [DataContract]、成员标记 [DataMember]。
    /// </summary>
    public static class Json
    {
        public static string Serialize<T>(T obj, bool indent = true)
        {
            using (var ms = new MemoryStream())
            {
                var ser = new DataContractJsonSerializer(typeof(T));
                ser.WriteObject(ms, obj);
                byte[] bytes = ms.ToArray();
                string text = Encoding.UTF8.GetString(bytes, 0, bytes.Length);
                return indent ? PrettyPrint(text) : text;
            }
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default(T);

            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var ser = new DataContractJsonSerializer(typeof(T));
                return (T)ser.ReadObject(ms);
            }
        }

        public static void SaveToFile<T>(string path, T obj)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, Serialize(obj), Encoding.UTF8);
        }

        public static T LoadFromFile<T>(string path)
        {
            if (!File.Exists(path)) return default(T);
            return Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }

        /// <summary>DataContractJsonSerializer 输出无换行，这里做一次简单的缩进整理。</summary>
        private static string PrettyPrint(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;

            var sb = new StringBuilder();
            int indent = 0;
            bool inString = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[i + 1]); i++; }
                    else if (c == '"') inString = false;
                    continue;
                }

                switch (c)
                {
                    case '"': sb.Append(c); inString = true; break;
                    case '{':
                    case '[':
                        sb.Append(c).Append(Environment.NewLine).Append(new string(' ', ++indent * 2));
                        break;
                    case '}':
                    case ']':
                        sb.Append(Environment.NewLine).Append(new string(' ', --indent * 2)).Append(c);
                        break;
                    case ',':
                        sb.Append(c).Append(Environment.NewLine).Append(new string(' ', indent * 2));
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
