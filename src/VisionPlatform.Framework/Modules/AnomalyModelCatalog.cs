using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VisionPlatform.Framework.Modules
{
    /// <summary>
    /// 一个异常检测模型的元信息（界面显示用；算法侧的真源是原生库读同一份 model.json）。
    /// </summary>
    public sealed class AnomalyModelInfo
    {
        /// <summary>目录名（= 参数里存的值，也是下拉框的 Key）。</summary>
        public string Name { get; set; }

        /// <summary>绝对目录。</summary>
        public string Dir { get; set; }

        /// <summary>0 = PaDiM，1 = EfficientAD。</summary>
        public int Kind { get; set; }

        /// <summary>"PaDiM" / "EfficientAD"（未知类型原样显示）。</summary>
        public string KindText
        {
            get
            {
                if (Kind == AnomalyModelCatalog.KindPaDiM) return "PaDiM";
                if (Kind == AnomalyModelCatalog.KindEfficientAD) return "EfficientAD";
                return "未知";
            }
        }

        public int Samples { get; set; }
        public double Threshold { get; set; }

        /// <summary>带关键数据的短描述，作为下拉项文字。</summary>
        public string Summary
        {
            get
            {
                string s = KindText;
                if (Kind == AnomalyModelCatalog.KindPaDiM && Samples > 0)
                    s += " · " + Samples.ToString(CultureInfo.InvariantCulture) + " 张";
                if (Threshold > 0)
                    s += " · 阈值 " + Threshold.ToString("0.##", CultureInfo.InvariantCulture);
                return s;
            }
        }

        public override string ToString() { return Name; }
    }

    /// <summary>
    /// 异常检测模型的**固定目录**约定。
    /// <para>
    /// 【为什么固定】现场部署时模型随软件一起分发，参数里只存"目录名"。
    /// 选路径的对话框一旦存在，就会出现"这个流程指向 D:\临时\xx"这种换台机器就失效的配置。
    /// 目录结构见 <c>docs/算法/异常检测-模型目录与训练.md</c>：
    /// <code>
    /// &lt;程序目录&gt;\Models\Anomaly\&lt;模型名&gt;\model.json  (+ padim.bin / *.xml / *.bin)
    /// </code>
    /// </para>
    /// <para>
    /// 【放在 Framework 的理由】模块包（下拉项）、宿主执行器（按名解析目录）、
    /// 训练窗体（写出新模型）三处都要用同一套解析规则；任何一处各写一份，
    /// 迟早在"谁能看见哪个模型"上出现分歧。
    /// </para>
    /// </summary>
    public static class AnomalyModelCatalog
    {
        public const int KindPaDiM = 0;
        public const int KindEfficientAD = 1;

        /// <summary>模型根目录名（相对程序目录）。</summary>
        public const string RootFolder = "Anomaly";

        private static string _rootOverride;

        /// <summary>模型根目录（绝对路径）。可用 <see cref="SetRootForTest"/> 覆盖（冒烟测试用）。</summary>
        public static string RootDir
        {
            get
            {
                if (!string.IsNullOrEmpty(_rootOverride)) return _rootOverride;
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
                return Path.Combine(Path.Combine(Path.Combine(baseDir, "Models"), RootFolder), "");
            }
        }

        /// <summary>仅测试用：把根目录指到临时目录；传 null 恢复默认。</summary>
        public static void SetRootForTest(string root)
        {
            _rootOverride = root;
        }

        /// <summary>确保根目录存在，返回绝对路径。</summary>
        public static string EnsureRoot()
        {
            string root = RootDir;
            try { if (!Directory.Exists(root)) Directory.CreateDirectory(root); }
            catch (Exception) { }
            return root;
        }

        /// <summary>
        /// 把参数里的值解析成绝对目录：
        /// 空 → 根目录下第一个可用模型；已是绝对路径 → 原样；否则 → 根目录 + 名字。
        /// </summary>
        public static string Resolve(string name)
        {
            string v = (name ?? "").Trim();
            string root = RootDir;

            if (v.Length == 0)
            {
                List<AnomalyModelInfo> all = List();
                return all.Count > 0 ? all[0].Dir : "";
            }

            try
            {
                if (Path.IsPathRooted(v)) return v;
            }
            catch (Exception) { }

            return Path.Combine(root, v);
        }

        /// <summary>扫描根目录下所有含 model.json 的子目录。</summary>
        public static List<AnomalyModelInfo> List()
        {
            var list = new List<AnomalyModelInfo>();
            string root = RootDir;

            try
            {
                if (!Directory.Exists(root)) return list;

                string[] dirs = Directory.GetDirectories(root);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);

                foreach (string d in dirs)
                {
                    AnomalyModelInfo info = ReadMeta(d);
                    if (info != null) list.Add(info);
                }
            }
            catch (Exception) { }

            return list;
        }

        /// <summary>取单个模型信息；不存在 / 不是模型目录返回 null。</summary>
        public static AnomalyModelInfo Describe(string name)
        {
            string dir = Resolve(name);
            return string.IsNullOrEmpty(dir) ? null : ReadMeta(dir);
        }

        /// <summary>
        /// 读 model.json 的少量字段。解析刻意"笨"：只找 Key 后的第一个值，
        /// 不引入 JSON 依赖 —— 这份文件是本软件自己写的，格式固定；
        /// 格式真出问题时算法侧加载会明确报错，界面这里降级为只显示目录名即可。
        /// </summary>
        private static AnomalyModelInfo ReadMeta(string dir)
        {
            try
            {
                string json = Path.Combine(dir, "model.json");
                if (!File.Exists(json)) return null;

                string text = File.ReadAllText(json, Encoding.UTF8);
                var info = new AnomalyModelInfo
                {
                    Name = Path.GetFileName(dir.TrimEnd('\\', '/')),
                    Dir = dir,
                    Kind = ReadInt(text, "kind", -1),
                    Samples = ReadInt(text, "samples", 0),
                    Threshold = ReadDouble(text, "threshold", 0)
                };
                return info.Kind < 0 ? null : info;
            }
            catch (Exception) { return null; }
        }

        private static string ValuePos(string json, string key)
        {
            int p = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (p < 0) return null;
            p = json.IndexOf(':', p);
            if (p < 0) return null;
            p++;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\t'
                                       || json[p] == '\r' || json[p] == '\n')) p++;
            return p < json.Length ? json.Substring(p) : null;
        }

        private static int ReadInt(string json, string key, int def)
        {
            string v = ValuePos(json, key);
            int n;
            if (v != null && int.TryParse(v.TrimStart().Split(',', '}', '\r', '\n')[0].Trim(),
                                          NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;
            return def;
        }

        private static double ReadDouble(string json, string key, double def)
        {
            string v = ValuePos(json, key);
            double d;
            if (v != null && double.TryParse(v.TrimStart().Split(',', '}', '\r', '\n')[0].Trim(),
                                             NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return d;
            return def;
        }

        /// <summary>把模型名规范成合法目录名（去掉路径分隔符与非法字符）。</summary>
        public static string SanitizeName(string name)
        {
            string v = (name ?? "").Trim();
            if (v.Length == 0) return "";

            var sb = new StringBuilder(v.Length);
            char[] bad = Path.GetInvalidFileNameChars();
            foreach (char c in v)
            {
                bool ok = true;
                foreach (char b in bad) if (c == b) { ok = false; break; }
                sb.Append(ok ? c : '_');
            }

            string s = sb.ToString().Trim();
            if (s.Length > 64) s = s.Substring(0, 64);
            return s;
        }
    }
}
