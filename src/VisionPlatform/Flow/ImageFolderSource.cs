using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VisionPlatform.Flow
{
    /// <summary>
    /// 文件夹图像源：把目录里的图片按文件名排成一个循环序列，供「图像源」节点逐张轮播。
    ///
    /// 【为什么要有缓存】连续运行时每个周期都会取一次图（≤30 fps）。如果每周期都
    /// <c>Directory.GetFiles</c> 一遍，几百张的目录会白烧几毫秒在文件系统上。这里按
    /// 「目录绝对路径 + 目录最后写入时间」缓存结果，目录内容变了（拷进新图）自动重扫。
    /// </summary>
    public static class ImageFolderSource
    {
        /// <summary>支持的扩展名（与 <c>OpenFileDialog</c> 的图片过滤器保持一致）。</summary>
        private static readonly string[] Exts =
        {
            ".bmp", ".jpg", ".jpeg", ".png", ".tif", ".tiff"
        };

        private sealed class CacheEntry
        {
            public string[] Files = new string[0];
            public DateTime StampUtc = DateTime.MinValue;
            public bool Valid;
        }

        private static readonly object _sync = new object();
        private static readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 列出目录下的图片文件（按文件名自然序，见 <see cref="NaturalCompare"/>）。
        /// 目录不存在 / 无权限 / 没有图片时返回空数组，<paramref name="error"/> 给出原因。
        /// </summary>
        public static string[] List(string folder, out string error)
        {
            return List(folder, Exts, "图片", out error);
        }

        /// <summary>
        /// 按指定扩展名列出目录文件（自然序）。缓存键包含扩展名约定（点云/图片共用同一套缓存字典）。
        /// </summary>
        public static string[] List(string folder, string[] extensions, string kindName, out string error)
        {
            error = "";

            if (string.IsNullOrWhiteSpace(folder))
            {
                error = "未选择" + kindName + "文件夹";
                return new string[0];
            }

            string dir;
            try { dir = Path.GetFullPath(folder.Trim()); }
            catch (Exception ex) { error = "文件夹路径非法（" + ex.Message + "）"; return new string[0]; }

            if (!Directory.Exists(dir))
            {
                error = "文件夹不存在（" + dir + "）";
                return new string[0];
            }

            DateTime stamp;
            try { stamp = Directory.GetLastWriteTimeUtc(dir); }
            catch (Exception) { stamp = DateTime.MinValue; }

            lock (_sync)
            {
                // 缓存键带上扩展名约定：同一目录里既有图片又有 PLY/PCD 时不会互相串
                string cacheKey = dir + "\u0001" + string.Join(";", extensions);
                CacheEntry hit;
                if (_cache.TryGetValue(cacheKey, out hit) && hit.Valid && hit.StampUtc == stamp && hit.Files.Length > 0)
                    return hit.Files;
            }

            string[] found;
            try { found = Directory.GetFiles(dir); }
            catch (Exception ex)
            {
                error = "读取文件夹失败（" + ex.Message + "）";
                return new string[0];
            }

            var list = new List<string>(found.Length);
            foreach (string f in found)
            {
                string ext = Path.GetExtension(f);
                if (string.IsNullOrEmpty(ext)) continue;

                bool ok = false;
                foreach (string e in extensions)
                {
                    if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase)) { ok = true; break; }
                }
                if (ok) list.Add(f);
            }

            // 排序口径：文件名自然序（img_2 在 img_10 之前）。用 Ordinal 排序会出现
            // "img_10 排在 img_2 前面"这种与现场直觉相反的顺序，排查时极易误判成"漏图"。
            list.Sort(delegate(string a, string b)
            {
                return NaturalCompare(Path.GetFileName(a), Path.GetFileName(b));
            });

            string[] result = list.ToArray();

            lock (_sync)
            {
                var e = new CacheEntry();
                e.Files = result;
                e.StampUtc = stamp;
                e.Valid = true;
                _cache[dir + "\u0001" + string.Join(";", extensions)] = e;
            }

            if (result.Length == 0) error = "文件夹内没有" + kindName + "（" + dir + "）";
            return result;
        }

        /// <summary>清空目录缓存（换目录 / 冒烟用）。</summary>
        public static void ClearCache()
        {
            lock (_sync) { _cache.Clear(); }
        }

        /// <summary>
        /// 文件名自然序比较：数字段按数值比（2 &lt; 10），其余按 Ordinal 忽略大小写。
        /// 例：<c>a2.bmp &lt; a10.bmp</c>，<c>a10.bmp &lt; b1.bmp</c>。
        /// </summary>
        public static int NaturalCompare(string a, string b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                char ca = a[i], cb = b[j];

                if (char.IsDigit(ca) && char.IsDigit(cb))
                {
                    // 跳过前导 0，再比数值长度（长度不同则长的更大），最后逐位比
                    int si = i, sj = j;
                    while (si < a.Length && a[si] == '0') si++;
                    while (sj < b.Length && b[sj] == '0') sj++;

                    int ei = si, ej = sj;
                    while (ei < a.Length && char.IsDigit(a[ei])) ei++;
                    while (ej < b.Length && char.IsDigit(b[ej])) ej++;

                    int la = ei - si, lb = ej - sj;
                    if (la != lb) return la < lb ? -1 : 1;

                    for (int k = 0; k < la; k++)
                    {
                        if (a[si + k] != b[sj + k]) return a[si + k] < b[sj + k] ? -1 : 1;
                    }

                    i = ei;
                    j = ej;
                    continue;
                }

                char ua = char.ToUpperInvariant(ca);
                char ub = char.ToUpperInvariant(cb);
                if (ua != ub) return ua < ub ? -1 : 1;

                i++;
                j++;
            }

            if (i < a.Length) return 1;
            if (j < b.Length) return -1;
            return 0;
        }
    }

    /// <summary>
    /// 文件夹图像源的「播放位置」。
    ///
    /// 【为什么要独立于节点参数】位置是<strong>运行期状态</strong>，不是配置：写进节点参数会
    /// 让每个周期都触发一次 <c>ConfigStore.Save()</c>（连续运行 30 fps → 每秒 30 次写盘），
    /// 而且会和"用户改参数"共用同一条落盘路径，容易把配置写脏。
    ///
    /// 【键的口径】<c>流程Id | 节点Id | 文件夹</c>：
    ///   · 带上流程/节点 → 同一条流程里的两个图像源节点各自独立计数；
    ///   · 带上文件夹 → 换了目录自然从第一张开始（不会沿用旧目录的索引导致"跳过前几张"）。
    ///
    /// 【持久化】节点参数 <c>FolderPersist=1</c> 时，索引落到
    /// <c>%AppData%\VisionPlatform\imagefolder_cursor.txt</c>；默认只在本进程内有效。
    /// 写盘做了节流（≥1 秒一次），否则连续运行会变成磁盘写压力。
    /// </summary>
    public static class ImageFolderCursor
    {
        private static readonly object _sync = new object();
        private static readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);

        // 每张图"开始显示的时间"：配合「每张停留(ms)」决定这一帧要不要换图。
        // 连续运行时每周期都会 Next 一次，没有这个节流就会 30fps 刷图，人眼根本看不清。
        private static readonly Dictionary<string, DateTime> _shownAt =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);

        // 上一次"实际返回给界面显示"的索引。停留期内必须返回它 ——
        // 若返回游标（已经 +1），停留就变成了"下一张已经在显示、只是没再推进"，
        // 表现是每点一次还是换图（这正是第一版 hold 失效的原因）。
        private static readonly Dictionary<string, int> _shownIndex =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private static bool _loaded;
        private static bool _dirty;
        private static DateTime _lastFlushUtc = DateTime.MinValue;

        /// <summary>落盘节流间隔：连续运行时最多每秒写一次。</summary>
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

        private static string _storeFile;   // null = 用默认路径

        /// <summary>
        /// 游标文件路径。默认 <c>%AppData%\VisionPlatform\imagefolder_cursor.txt</c>；
        /// 冒烟测试会改指到临时目录（不能动用户真实的播放位置），改指时同时清空内存态。
        /// </summary>
        public static string StoreFile
        {
            get
            {
                if (_storeFile != null) return _storeFile;
                return Path.Combine(Core.AppPaths.Root, "imagefolder_cursor.txt");
            }
            set
            {
                lock (_sync)
                {
                    _storeFile = value;
                    _index.Clear();
                    _loaded = false;
                    _dirty = false;
                }
            }
        }

        /// <summary>
        /// 游标键：<c>流程Id | 节点Id | 文件夹绝对路径</c>。
        /// <para>
        /// 运行期（<see cref="FlowRunner"/>）与界面预览（<c>FlowForm</c>）必须用<strong>同一个</strong>
        /// 键，否则"预览显示第 3 张、运行却取了第 1 张"，看起来就像索引没生效。所以键只在这里拼一次。
        /// </para>
        /// </summary>
        public static string Key(FlowDefinition flow, FlowNode node, string folder)
        {
            string fid = "";
            if (flow != null)
                fid = !string.IsNullOrEmpty(flow.Id) ? flow.Id : (flow.Name ?? "");

            string full = folder ?? "";
            if (!string.IsNullOrWhiteSpace(full))
            {
                try { full = Path.GetFullPath(full.Trim()); }
                catch (Exception) { /* 路径非法时按原样当键，运行期会给出"文件夹不存在" */ }
            }

            return fid + "|" + ((node != null && node.Id != null) ? node.Id : "") + "|" + full;
        }

        /// <summary>
        /// 取当前索引（不推进）。<paramref name="count"/> 为序列长度；索引越界时按取模归一。
        /// 用于「预览区选中节点时先把当前这张显示出来」。
        /// </summary>
        public static int Peek(string key, int count)
        {
            if (count <= 0) return 0;

            lock (_sync)
            {
                EnsureLoaded();

                int v;
                if (!_index.TryGetValue(key ?? "", out v)) v = 0;
                return ((v % count) + count) % count;
            }
        }

        /// <summary>
        /// 取当前索引并推进一位（到末尾回到 0）。返回本次应当使用的位置。
        /// </summary>
        /// <param name="persist">true = 落盘（节点参数「记住播放位置」）。</param>
        /// <param name="holdMs">
        /// 同一张图的最小停留时间（毫秒）；<c>0</c> = 每个周期换一张。
        /// 连续运行 30fps 时若不设停留，图片会以肉眼看不清的速度狂翻。
        /// </param>
        public static int Next(string key, int count, bool persist)
        {
            return Next(key, count, persist, 0);
        }

        /// <summary>带"每张停留"的取图：停留未到就复用当前这张，不推进索引。</summary>
        public static int Next(string key, int count, bool persist, int holdMs)
        {
            if (count <= 0) return 0;
            if (key == null) key = "";

            lock (_sync)
            {
                EnsureLoaded();

                int v;
                if (!_index.TryGetValue(key, out v)) v = 0;
                v = ((v % count) + count) % count;

                DateTime shown;
                bool hasShown = _shownAt.TryGetValue(key, out shown);
                if (holdMs > 0 && hasShown && (DateTime.UtcNow - shown).TotalMilliseconds < holdMs)
                {
                    int shownIdx;
                    return _shownIndex.TryGetValue(key, out shownIdx) ? shownIdx : v;
                }

                _shownAt[key] = DateTime.UtcNow;
                _shownIndex[key] = v;

                int next = v + 1;
                if (next >= count) next = 0;
                _index[key] = next;

                if (persist)
                {
                    _dirty = true;
                    FlushIfDue();
                }

                return v;
            }
        }

        /// <summary>把某个键复位到第一张（「重新示教」式的入口用）。</summary>
        public static void Reset(string key)
        {
            lock (_sync)
            {
                EnsureLoaded();
                _index[key ?? ""] = 0;
                _shownAt.Remove(key ?? "");
                _shownIndex.Remove(key ?? "");
                _dirty = true;
                FlushIfDue();
            }
        }

        /// <summary>清空全部游标（冒烟 / 换配方用）。</summary>
        public static void Clear(bool flush)
        {
            lock (_sync)
            {
                _index.Clear();
                _shownAt.Clear();
                _shownIndex.Clear();
                _loaded = true;
                _dirty = flush;
                if (flush) Flush();
            }
        }

        /// <summary>立即落盘（关窗前调用，避免最后一个索引丢失）。</summary>
        public static void Flush()
        {
            lock (_sync)
            {
                if (!_dirty) return;

                try
                {
                    string file = StoreFile;
                    string dir = Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var sb = new StringBuilder();
                    sb.AppendLine("# VisionSystem 图像源文件夹游标：<Base64(键)> <下一张索引>");
                    foreach (KeyValuePair<string, int> kv in _index)
                    {
                        sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Key)));
                        sb.Append(' ');
                        sb.Append(kv.Value.ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine();
                    }

                    File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
                    _dirty = false;
                    _lastFlushUtc = DateTime.UtcNow;
                }
                catch (Exception ex)
                {
                    // 落盘失败不能影响检测：下次 Next 会再试
                    Core.Logger.Warn("图像源游标落盘失败：" + ex.Message);
                }
            }
        }

        // ------------------------------------------------------------------

        private static void FlushIfDue()
        {
            if (!_dirty) return;
            if (DateTime.UtcNow - _lastFlushUtc < FlushInterval) return;
            Flush();
        }

        /// <summary>首次访问时读入游标文件（不存在 / 损坏都静默按空处理）。</summary>
        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                string file = StoreFile;
                if (!File.Exists(file)) return;

                foreach (string raw in File.ReadAllLines(file, Encoding.UTF8))
                {
                    string line = (raw ?? "").Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int sp = line.LastIndexOf(' ');
                    if (sp <= 0 || sp >= line.Length - 1) continue;

                    string key;
                    try { key = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(0, sp))); }
                    catch (Exception) { continue; }

                    int idx;
                    if (!int.TryParse(line.Substring(sp + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
                        continue;

                    _index[key] = idx < 0 ? 0 : idx;
                }
            }
            catch (Exception ex)
            {
                Core.Logger.Warn("图像源游标读取失败：" + ex.Message);
            }
        }
    }
}
