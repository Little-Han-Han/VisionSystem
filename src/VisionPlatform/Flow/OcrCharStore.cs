using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using VisionPlatform.Core;
using VisionPlatform.Native;

namespace VisionPlatform.Flow
{
    /// <summary>一张模板文件在磁盘上的形态（扫描结果，只读）。</summary>
    public sealed class OcrCharFile
    {
        /// <summary>绝对路径。</summary>
        public string Path = "";

        /// <summary>标签 = 文件名去扩展名（<strong>唯一真源</strong>，见设计 §2.4）。</summary>
        public string Label = "";

        /// <summary>扩展名（小写，含点）。</summary>
        public string Ext = "";

        /// <summary>文件字节数。</summary>
        public long Length;

        /// <summary>最后写入时间（本地时间，界面显示用）。</summary>
        public DateTime LastWrite = DateTime.MinValue;

        /// <summary>像素宽；读不到时为 0（同时会进失败清单）。</summary>
        public int PixelW;

        /// <summary>像素高。</summary>
        public int PixelH;
    }

    /// <summary>按标签归组后的一项（界面一行）。</summary>
    public sealed class OcrCharEntry
    {
        /// <summary>标签（= 文件名去扩展名）。</summary>
        public string Label = "";

        /// <summary>同标签的样本文件（<strong>同标签只能靠"同名不同扩展名"扩容</strong>，最多 6 份）。</summary>
        public List<OcrCharFile> Files = new List<OcrCharFile>();

        /// <summary>样本张数。</summary>
        public int SampleCount { get { return Files.Count; } }

        /// <summary>该标签下最新的写入时间。</summary>
        public DateTime LastWrite
        {
            get
            {
                DateTime t = DateTime.MinValue;
                foreach (OcrCharFile f in Files)
                    if (f != null && f.LastWrite > t) t = f.LastWrite;
                return t;
            }
        }

        /// <summary>取缩略图的那一份（按扩展名白名单顺序取第一份）。</summary>
        public string PrimaryPath
        {
            get
            {
                OcrCharFile best = null;
                int bestRank = int.MaxValue;
                foreach (OcrCharFile f in Files)
                {
                    if (f == null) continue;
                    int rank = OcrCharStore.ExtRank(f.Ext);
                    if (rank < bestRank) { bestRank = rank; best = f; }
                }
                return best != null ? best.Path : "";
            }
        }
    }

    /// <summary>扫描失败的分类。<strong>判定顺序不能乱</strong>，详见 <see cref="OcrCharStore.Scan"/>。</summary>
    public enum OcrCharFailureKind
    {
        /// <summary>不是有效图像。</summary>
        NotImage = 0,

        /// <summary>0 字节。</summary>
        Empty = 1,

        /// <summary>扩展名不在白名单。</summary>
        UnsupportedExt = 2,

        /// <summary>超过 256 KB。</summary>
        TooLarge = 3,

        /// <summary>被占用 / 无读权限。</summary>
        AccessDenied = 4
    }

    /// <summary>一条扫描失败记录（<strong>所有失败都必须出现在清单里，禁止静默</strong>）。</summary>
    public sealed class OcrCharFailure
    {
        /// <summary>只存文件名（界面显示 + 可复制，不暴露完整路径）。</summary>
        public string FileName = "";

        /// <summary>失败分类。</summary>
        public OcrCharFailureKind Kind;

        /// <summary>中文、可行动的原因。</summary>
        public string Reason = "";
    }

    /// <summary>一次目录扫描的完整结果。</summary>
    public sealed class OcrCharLibrary
    {
        /// <summary>被扫描的目录（绝对路径）。</summary>
        public string Dir = "";

        /// <summary>按标签归组的条目（按 <see cref="string.CompareOrdinal"/> 排序）。</summary>
        public List<OcrCharEntry> Entries = new List<OcrCharEntry>();

        /// <summary>失败清单。</summary>
        public List<OcrCharFailure> Failures = new List<OcrCharFailure>();

        /// <summary>合法扩展名的文件总数（含失败的）。</summary>
        public int FileCount;

        /// <summary>扫描时间。</summary>
        public DateTime ScannedAt = DateTime.MinValue;

        /// <summary>字符种类数（= "37 个字符" 里的 37）。</summary>
        public int LabelCount { get { return Entries.Count; } }

        /// <summary>样本总张数（= "92 张样本" 里的 92）。</summary>
        public int SampleCount
        {
            get
            {
                int n = 0;
                foreach (OcrCharEntry e in Entries) if (e != null) n += e.SampleCount;
                return n;
            }
        }

        /// <summary>目录里一个合法模板文件都没有。</summary>
        public bool IsEmpty { get { return FileCount == 0; } }

        /// <summary>有文件但一张都没加载成功（全是坏图）。</summary>
        public bool AllFailed { get { return FileCount > 0 && Entries.Count == 0; } }

        /// <summary>该标签已有几份样本（入库前查重用）。</summary>
        public int CountOf(string label)
        {
            if (string.IsNullOrEmpty(label)) return 0;
            foreach (OcrCharEntry e in Entries)
                if (e != null && string.Equals(e.Label, label, StringComparison.Ordinal)) return e.SampleCount;
            return 0;
        }
    }

    /// <summary>标签已存在时的入库策略。</summary>
    public enum OcrCharAddMode
    {
        /// <summary>追加一份样本：依次找第一个空闲扩展名；6 个都占满则失败。</summary>
        Append = 0,

        /// <summary>覆盖主样本（<c>label.png</c>）。</summary>
        Replace = 1,

        /// <summary>已存在就放弃。</summary>
        Abort = 2
    }

    /// <summary>迁移结果。</summary>
    public sealed class OcrCharMigrateResult
    {
        /// <summary>成功复制的文件数。</summary>
        public int Copied;

        /// <summary>失败的文件数。</summary>
        public int Failed;

        /// <summary>目标已存在同名文件而跳过的数量。</summary>
        public int Skipped;

        /// <summary>失败明细。</summary>
        public List<OcrCharFailure> Problems = new List<OcrCharFailure>();
    }

    /// <summary>
    /// OCR 字符模板库的磁盘层：<strong>目录就是库</strong>，不引入数据库 / 索引文件。
    /// <para>
    /// 【规则一条都不能改】**标签 = 文件名去扩展名**。所以"同一字符多份样本"只能靠
    /// <strong>同名不同扩展名</strong>（<c>A.png</c> + <c>A.bmp</c>），扩展名白名单 6 个 → 每字符上限 6 份。
    /// <c>A_001.png</c> 的标签是 <c>A_001</c> 而不是 <c>A</c> —— <strong>不存在任何后缀归组方案</strong>。
    /// </para>
    /// <para>
    /// 【归一化落在读取侧】手工放进目录的图（截图工具存出来的 24bpp PNG）走的是加载路径、
    /// 压根不经过入库归一化，所以 <see cref="LoadThumbnail"/> 与运行时加载都必须读回后转身份灰度。
    /// </para>
    /// </summary>
    public static class OcrCharStore
    {
        /// <summary>支持的扩展名（顺序即"追加时的优先顺序"，也是缩略图取样的优先级）。</summary>
        public static readonly string[] Extensions = { ".png", ".bmp", ".tif", ".tiff", ".jpg", ".jpeg" };

        /// <summary>单个模板文件体积上限（256 KB）。</summary>
        public const long MaxBytes = 256L * 1024L;

        /// <summary>模板最小边长（像素）。</summary>
        public const int MinSide = 3;

        /// <summary>模板最大边长（像素）。</summary>
        public const int MaxSide = 512;

        /// <summary>
        /// 标签的 UTF-8 字节上限。<strong>原生库会把 label 静默截断到 7 字节</strong>，
        /// 不校验的话 <c>华东区</c>（9 字节）与 <c>华北区</c> 会被截成同一个标签而静默合并 ——
        /// 这比覆盖更难查，所以必须在入库前拦住。
        /// </summary>
        public const int MaxLabelBytes = 7;

        // =====================================================================
        //  标签
        // =====================================================================

        /// <summary>标签校验：合法返回 null，非法返回中文原因。</summary>
        public static string ValidateLabel(string label)
        {
            if (label == null) return "标签不能为空";

            string s = label.Trim();
            if (s.Length == 0) return "标签不能为空";

            char[] bad = Path.GetInvalidFileNameChars();
            for (int i = 0; i < s.Length; i++)
            {
                for (int k = 0; k < bad.Length; k++)
                    if (s[i] == bad[k])
                        return "标签不能包含 " + bad[k] + " 等文件名非法字符";
            }

            int bytes = Encoding.UTF8.GetByteCount(s);
            if (bytes > MaxLabelBytes)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "标签最多 {0} 字节（UTF-8），「{1}」是 {2} 字节；中文每字 3 字节，即最多 2 个汉字。"
                    + "超过会被原生库静默截断，导致不同标签被合并成同一个字符。",
                    MaxLabelBytes, s, bytes);
            }

            return null;
        }

        /// <summary>标签的 UTF-8 字节数（界面显示"已用 x/7 字节"）。</summary>
        public static int LabelByteCount(string label)
        {
            return string.IsNullOrEmpty(label) ? 0 : Encoding.UTF8.GetByteCount(label.Trim());
        }

        /// <summary>扩展名在优先级列表里的位次（不在列表里返回 <see cref="int.MaxValue"/>）。</summary>
        public static int ExtRank(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return int.MaxValue;
            string e = ext.ToLowerInvariant();
            for (int i = 0; i < Extensions.Length; i++)
                if (Extensions[i] == e) return i;
            return int.MaxValue;
        }

        /// <summary>是否为支持的扩展名。</summary>
        public static bool IsSupportedExt(string ext)
        {
            return ExtRank(ext) != int.MaxValue;
        }

        // =====================================================================
        //  扫描
        // =====================================================================

        /// <summary>
        /// 扫描目录（<strong>只读尺寸，不取缩略图</strong>）。目录不存在时返回空库（不抛异常）。
        /// <para>
        /// 失败分类的<strong>判定顺序不能乱</strong>：<c>Empty</c> → <c>UnsupportedExt</c> →
        /// <c>TooLarge</c> → <c>NotImage</c> → <c>AccessDenied</c>。
        /// 例：<c>x.bmp</c> 若是 0 字节，必须先命中"0 字节"而不是"不是有效图像"——
        /// 后者会让用户去查图片内容，方向全错。
        /// </para>
        /// </summary>
        public static OcrCharLibrary Scan(string dir)
        {
            var lib = new OcrCharLibrary();
            lib.Dir = dir ?? "";
            lib.ScannedAt = DateTime.Now;

            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return lib;

            string[] all;
            try
            {
                all = Directory.GetFiles(dir);
            }
            catch (Exception ex)
            {
                lib.Failures.Add(new OcrCharFailure
                {
                    FileName = dir,
                    Kind = OcrCharFailureKind.AccessDenied,
                    Reason = "目录无法读取：" + ex.Message
                });
                return lib;
            }

            var byLabel = new Dictionary<string, OcrCharEntry>(StringComparer.Ordinal);

            foreach (string path in all)
            {
                if (string.IsNullOrEmpty(path)) continue;

                string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
                if (!IsSupportedExt(ext)) continue;      // 非模板文件（如说明 txt）直接忽略，不算失败

                lib.FileCount++;

                var fi = new FileInfo(path);
                string name = Path.GetFileName(path);
                string label = Path.GetFileNameWithoutExtension(path);

                // ① 0 字节
                if (fi.Length == 0)
                {
                    lib.Failures.Add(MakeFailure(name, OcrCharFailureKind.Empty, "0 字节"));
                    continue;
                }

                // ② 体积超限（256 KB）
                if (fi.Length > MaxBytes)
                {
                    lib.Failures.Add(MakeFailure(name, OcrCharFailureKind.TooLarge,
                        "文件 " + FormatKb(fi.Length) + "，超过上限 " + FormatKb(MaxBytes)));
                    continue;
                }

                if (string.IsNullOrEmpty(label))
                {
                    lib.Failures.Add(MakeFailure(name, OcrCharFailureKind.NotImage, "文件名为空，无法作为标签"));
                    continue;
                }

                // ③ 真读一次拿尺寸（只读尺寸，读完立刻释放）
                int w = 0, h = 0;
                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var probe = new Bitmap(fs))
                    {
                        w = probe.Width;
                        h = probe.Height;
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    lib.Failures.Add(MakeFailure(name, OcrCharFailureKind.AccessDenied, "文件被占用或无读权限：" + ex.Message));
                    continue;
                }
                catch (Exception ex)
                {
                    lib.Failures.Add(MakeFailure(name, OcrCharFailureKind.NotImage, "不是有效图像：" + ex.Message));
                    continue;
                }

                if (w <= 0 || h <= 0)
                {
                    lib.Failures.Add(MakeFailure(name, OcrCharFailureKind.NotImage, "图像尺寸无效"));
                    continue;
                }

                var file = new OcrCharFile
                {
                    Path = path,
                    Label = label,
                    Ext = ext,
                    Length = fi.Length,
                    LastWrite = fi.LastWriteTime,
                    PixelW = w,
                    PixelH = h
                };

                OcrCharEntry entry;
                if (!byLabel.TryGetValue(label, out entry))
                {
                    entry = new OcrCharEntry { Label = label };
                    byLabel[label] = entry;
                }
                entry.Files.Add(file);
            }

            foreach (OcrCharEntry e in byLabel.Values)
            {
                // 同一标签内按扩展名优先级排序，保证"主样本"稳定（缩略图取第一份）
                e.Files.Sort(delegate (OcrCharFile a, OcrCharFile b)
                {
                    int ra = ExtRank(a.Ext), rb = ExtRank(b.Ext);
                    if (ra != rb) return ra.CompareTo(rb);
                    return string.CompareOrdinal(a.Path, b.Path);
                });
                lib.Entries.Add(e);
            }

            lib.Entries.Sort(delegate (OcrCharEntry a, OcrCharEntry b)
            {
                return string.CompareOrdinal(a.Label, b.Label);
            });

            return lib;
        }

        private static OcrCharFailure MakeFailure(string fileName, OcrCharFailureKind kind, string reason)
        {
            return new OcrCharFailure { FileName = fileName, Kind = kind, Reason = reason };
        }

        private static string FormatKb(long bytes)
        {
            return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        }

        // =====================================================================
        //  入库
        // =====================================================================

        /// <summary>
        /// 加入一份样本：<strong>原始灰度、不预二值化、不缩放</strong>，PNG 无损落盘。
        /// 返回实际写入路径；失败返回 null 并给出中文原因。
        /// </summary>
        /// <param name="onExists">标签已存在时的处理方式。</param>
        public static string Add(string dir, string label, Bitmap sample, OcrCharAddMode onExists,
                                 out string reason)
        {
            reason = null;

            string labelError = ValidateLabel(label);
            if (labelError != null) { reason = labelError; return null; }
            if (sample == null) { reason = "没有可入库的图像"; return null; }
            if (string.IsNullOrWhiteSpace(dir)) { reason = "模板目录为空"; return null; }

            if (sample.Width < MinSide || sample.Height < MinSide)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "选区太小（{0}×{1}），至少 {2}×{2} 像素。", sample.Width, sample.Height, MinSide);
                return null;
            }
            if (sample.Width > MaxSide || sample.Height > MaxSide)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    "选区太大（{0}×{1}），单字符不应超过 {2}×{2} 像素，请重新框选。",
                    sample.Width, sample.Height, MaxSide);
                return null;
            }

            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                reason = "无法创建模板目录：" + ex.Message;
                return null;
            }

            string trimmed = label.Trim();
            string pngPath = Path.Combine(dir, trimmed + ".png");

            // 已存在时的策略
            if (File.Exists(pngPath))
            {
                if (onExists == OcrCharAddMode.Abort)
                {
                    reason = "该字符已有样本（" + trimmed + ".png），请选择「追加」或「覆盖」。";
                    return null;
                }
                if (onExists == OcrCharAddMode.Replace)
                {
                    return WriteSample(pngPath, sample, out reason);
                }
                // Append 且 png 已存在：先看内容是否完全一样（同图重复入库不该占第二个位置）
                if (SameContent(pngPath, sample))
                {
                    reason = "该字符已存在相同样本（" + trimmed + ".png），未新增。";
                    return pngPath;
                }
            }

            // 找第一个空闲扩展名（Append 语义）
            foreach (string ext in Extensions)
            {
                string path = Path.Combine(dir, trimmed + ext);
                if (!File.Exists(path)) return WriteSample(path, sample, out reason);
            }

            reason = "该字符已有 " + Extensions.Length + " 份样本（扩展名已用满），无法再追加。"
                     + "同一字符的多份样本只能靠同名不同扩展名区分。";
            return null;
        }

        /// <summary>写一份样本并<strong>回读校验</strong>：写成功但读不出 = 失败（删除刚写的文件）。</summary>
        private static string WriteSample(string path, Bitmap sample, out string reason)
        {
            reason = null;
            Bitmap gray = null;
            try
            {
                // 统一存 8bpp 身份灰度：不预二值化、不缩放（原生内部会做与识别完全相同的处理）
                gray = TemplateStore.IsIdentityGray(sample) ? new Bitmap(sample) : TemplateStore.ConvertToIdentityGray(sample);

                string tmp = path + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
                gray.Save(tmp, ImageFormat.Png);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);

                long length = new FileInfo(path).Length;
                if (length > MaxBytes)
                {
                    TryDelete(path);
                    reason = "模板文件 " + FormatKb(length) + " 超过上限 " + FormatKb(MaxBytes)
                             + "，请缩小选区或确认框选是否正确。";
                    return null;
                }

                // 回读校验：能解码 + 尺寸一致
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var probe = new Bitmap(fs))
                {
                    if (probe.Width != gray.Width || probe.Height != gray.Height)
                    {
                        TryDelete(path);
                        reason = string.Format(CultureInfo.InvariantCulture,
                            "写盘后回读尺寸不一致（应为 {0}×{1}，实际 {2}×{3}），已撤销。",
                            gray.Width, gray.Height, probe.Width, probe.Height);
                        return null;
                    }
                }

                return path;
            }
            catch (Exception ex)
            {
                TryDelete(path);
                reason = "写入模板失败：" + ex.Message;
                return null;
            }
            finally
            {
                if (gray != null) gray.Dispose();
            }
        }

        /// <summary>比较磁盘文件与候选样本是否"同一张图"（尺寸一致 + 逐像素一致）。</summary>
        private static bool SameContent(string path, Bitmap sample)
        {
            try
            {
                Bitmap disk = LoadThumbnail(path);
                if (disk == null) return false;

                try
                {
                    if (disk.Width != sample.Width || disk.Height != sample.Height) return false;

                    Bitmap candidate = TemplateStore.IsIdentityGray(sample)
                        ? sample
                        : TemplateStore.ConvertToIdentityGray(sample);
                    try
                    {
                        return TemplateStore.IsIdentityGray(disk)
                               && PixelEquals(disk, candidate);
                    }
                    finally
                    {
                        if (!ReferenceEquals(candidate, sample)) candidate.Dispose();
                    }
                }
                finally
                {
                    disk.Dispose();
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool PixelEquals(Bitmap a, Bitmap b)
        {
            if (a == null || b == null || a.Width != b.Width || a.Height != b.Height) return false;

            BitmapData da = null, db = null;
            try
            {
                var rect = new Rectangle(0, 0, a.Width, a.Height);
                da = a.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);
                db = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format8bppIndexed);

                var ra = new byte[da.Stride];
                var rb = new byte[db.Stride];

                for (int y = 0; y < a.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        IntPtr.Add(da.Scan0, y * da.Stride), ra, 0, da.Stride);
                    System.Runtime.InteropServices.Marshal.Copy(
                        IntPtr.Add(db.Scan0, y * db.Stride), rb, 0, db.Stride);

                    // 8bpp 下每字节就是一个像素，stride 尾部是行填充，不参与比较
                    for (int x = 0; x < a.Width; x++)
                        if (ra[x] != rb[x]) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (da != null) a.UnlockBits(da);
                if (db != null) b.UnlockBits(db);
            }
        }

        // =====================================================================
        //  删除 / 清空
        // =====================================================================

        /// <summary>删除某标签的全部样本。</summary>
        public static bool Delete(string dir, string label, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(label))
            {
                reason = "目录或标签为空";
                return false;
            }

            bool any = false;
            foreach (string ext in Extensions)
            {
                string path = Path.Combine(dir, label.Trim() + ext);
                if (File.Exists(path)) { TryDelete(path); any = true; }
            }

            if (!any) { reason = "没有找到「" + label + "」的样本文件"; return false; }

            reason = "已删除「" + label + "」的全部样本";
            return true;
        }

        /// <summary>删除单个文件。</summary>
        public static bool DeleteFile(string filePath, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                reason = "文件不存在";
                return false;
            }

            try
            {
                File.Delete(filePath);
                reason = "已删除 " + Path.GetFileName(filePath);
                return true;
            }
            catch (Exception ex)
            {
                reason = "删除失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>清空目录内全部合法模板文件（保留目录本身，不递归）。返回删除个数。</summary>
        public static int ClearAll(string dir, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                reason = "目录不存在";
                return 0;
            }

            int removed = 0;
            try
            {
                foreach (string path in Directory.GetFiles(dir))
                {
                    if (!IsSupportedExt(Path.GetExtension(path))) continue;
                    if (TryDelete(path)) removed++;
                }
            }
            catch (Exception ex)
            {
                reason = "清空失败：" + ex.Message;
                return removed;
            }

            reason = removed > 0 ? "已删除 " + removed + " 个模板文件" : "目录里没有模板文件";
            return removed;
        }

        private static bool TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) { File.Delete(path); return true; }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // =====================================================================
        //  缩略图（懒加载 + 读取侧归一化）
        // =====================================================================

        /// <summary>
        /// 读一张模板图供界面显示：<strong>先读后拷</strong>（不锁文件）+ <strong>转身份灰度</strong>
        /// （GDI+ 解码不保真，手工放进目录的 24bpp 图也走这条路径）。失败返回 null。
        /// </summary>
        public static Bitmap LoadThumbnail(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try
            {
                Bitmap raw = TemplateStore.LoadBitmapNoLock(path);
                if (raw == null) return null;

                if (TemplateStore.IsIdentityGray(raw)) return raw;

                Bitmap gray = TemplateStore.ConvertToIdentityGray(raw);
                raw.Dispose();
                return gray;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // =====================================================================
        //  迁移（复制，永不等于移动）
        // =====================================================================

        /// <summary>
        /// 把 <paramref name="fromDir"/> 下的合法模板<strong>复制</strong>到 <paramref name="toDir"/>。
        /// <para>
        /// 【硬约束】**实现里不得出现任何对源目录的写 / 删 / 改名**（连 <c>File.SetLastWriteTime</c>
        /// 都不许碰源）。<paramref name="deleteSource"/> 为 true 时，只删"已成功复制并回读校验通过"的那些，
        /// <strong>校验失败的文件即使勾了也不删</strong>（宁留不丢）。
        /// </para>
        /// </summary>
        public static OcrCharMigrateResult Migrate(string fromDir, string toDir, bool deleteSource)
        {
            var result = new OcrCharMigrateResult();

            if (string.IsNullOrWhiteSpace(fromDir) || string.IsNullOrWhiteSpace(toDir)
                || !Directory.Exists(fromDir))
            {
                result.Problems.Add(new OcrCharFailure
                {
                    FileName = fromDir ?? "",
                    Kind = OcrCharFailureKind.AccessDenied,
                    Reason = "源目录不存在或不可读"
                });
                result.Failed++;
                return result;
            }

            if (string.Equals(Path.GetFullPath(fromDir).TrimEnd('\\'),
                              Path.GetFullPath(toDir).TrimEnd('\\'),
                              StringComparison.OrdinalIgnoreCase))
            {
                result.Problems.Add(new OcrCharFailure
                {
                    FileName = fromDir,
                    Kind = OcrCharFailureKind.AccessDenied,
                    Reason = "源目录与目标目录相同，无需迁移"
                });
                result.Failed++;
                return result;
            }

            try { Directory.CreateDirectory(toDir); }
            catch (Exception ex)
            {
                result.Problems.Add(new OcrCharFailure
                {
                    FileName = toDir,
                    Kind = OcrCharFailureKind.AccessDenied,
                    Reason = "无法创建目标目录：" + ex.Message
                });
                result.Failed++;
                return result;
            }

            string[] files;
            try { files = Directory.GetFiles(fromDir); }
            catch (Exception ex)
            {
                result.Problems.Add(new OcrCharFailure
                {
                    FileName = fromDir,
                    Kind = OcrCharFailureKind.AccessDenied,
                    Reason = "源目录无法读取：" + ex.Message
                });
                result.Failed++;
                return result;
            }

            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string src in files)
            {
                string ext = (Path.GetExtension(src) ?? "").ToLowerInvariant();
                if (!IsSupportedExt(ext)) continue;

                string name = Path.GetFileName(src);
                string dst = Path.Combine(toDir, name);

                if (File.Exists(dst))
                {
                    result.Skipped++;      // 目标已有同名文件：不覆盖，交用户自己判断
                    continue;
                }

                try
                {
                    File.Copy(src, dst, false);

                    // 复制后回读校验（能打开 + 尺寸 > 0）。注意：这一步只读目标文件，源目录零改动。
                    bool ok;
                    try
                    {
                        using (var fs = new FileStream(dst, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var probe = new Bitmap(fs))
                            ok = probe.Width > 0 && probe.Height > 0;
                    }
                    catch (Exception)
                    {
                        ok = false;
                    }

                    if (!ok)
                    {
                        TryDelete(dst);
                        result.Failed++;
                        result.Problems.Add(new OcrCharFailure
                        {
                            FileName = name,
                            Kind = OcrCharFailureKind.NotImage,
                            Reason = "复制后回读校验失败（目标文件不可读），已删除目标副本"
                        });
                        continue;
                    }

                    result.Copied++;

                    // 只有"复制并校验通过"的文件才允许删源
                    if (deleteSource) TryDelete(src);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Problems.Add(new OcrCharFailure
                    {
                        FileName = name,
                        Kind = OcrCharFailureKind.AccessDenied,
                        Reason = "复制失败：" + ex.Message
                    });
                }
            }

            return result;
        }
    }
}
