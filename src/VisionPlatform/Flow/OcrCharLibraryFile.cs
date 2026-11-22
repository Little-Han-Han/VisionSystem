using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VisionPlatform.Flow
{
    /// <summary>字符库里的一个样本：标签 + 原始灰度像素。</summary>
    /// <remarks>
    /// 【为什么存原始灰度、不做归一化】归一化（缩放/二值化）是**识别时**的口径，属于算法参数；
    /// 存进库里就把当时的参数固化死了，以后调参数反而要重做全部样本。
    /// 这里只存"框出来是什么样"，由原生 <c>va_ocr_add_template</c> 在加载时统一归一化。
    /// </remarks>
    public sealed class OcrCharSample
    {
        /// <summary>字符标签（一个字符，如 "A" / "7" / "中"）。</summary>
        public string Label = "";

        public int Width;
        public int Height;

        /// <summary>Width×Height 的 8bpp 灰度像素（行优先，无行尾填充）。</summary>
        public byte[] Pixels = new byte[0];

        public OcrCharSample() { }

        public OcrCharSample(string label, int width, int height, byte[] pixels)
        {
            Label = label;
            Width = width;
            Height = height;
            Pixels = pixels ?? new byte[0];
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1}×{2}", Label, Width, Height);
        }
    }

    /// <summary>
    /// OCR 字符库的**单文件序列化格式**（<c>.ocrtpl</c>）。
    ///
    /// 【为什么不沿用"一个字符一张图"的目录】目录方案有三个绕不开的毛病：
    ///   1) 一个字符最多 6 份样本（靠同名不同扩展名），想多补几张只能换名字；
    ///   2) 目录里混进 readme、缩略图、系统生成的 Thumbs.db 时要靠扩展名白名单兜；
    ///   3) 库与"某条流程"没有绑定关系 —— 换条线要人工去搬目录，搬错了才发现跑错字。
    /// 单文件把"库"变成一个可命名、可随流程走的对象，追加样本就是往同一个文件里加条目。
    ///
    /// 【格式】小端二进制，头部 12 字节：
    /// <code>
    ///   "OCRT" | int32 version | int32 count
    ///   每条：int32 labelLen | label(UTF-8) | int32 w | int32 h | w*h 字节灰度
    /// </code>
    /// 选二进制而不是 JSON+base64：样本本身是二值图像，base64 要多占 33% 且没法直接比对；
    /// 而且标签里可能出现引号/反斜杠（现场有 "5" 与 "S" 混用的库），JSON 转义只会更容易出错。
    /// </remarks>
    public static class OcrCharLibraryFile
    {
        /// <summary>库文件扩展名。</summary>
        public const string Extension = ".ocrtpl";

        private const string Magic = "OCRT";
        private const int Version = 1;

        /// <summary>单条样本边长上限（防坏文件把内存吃爆）。</summary>
        private const int MaxSide = 4096;

        /// <summary>样本条数上限。</summary>
        private const int MaxSamples = 20000;

        /// <summary>
        /// 默认库文件路径：<c>OCRModel\&lt;流程标识&gt;.ocrtpl</c>。
        /// <para>
        /// 【为什么带流程标识】用户的要求是"不同流程用的字符库可以不同"。默认按流程分文件，
        /// 新建的流程天然拿到自己的库，不会出现"两条线共用一份、改了 A 线 B 线跟着变"。
        /// 想让两条流程共用同一个库，把它们节点的「库文件」指向同一个路径即可。
        /// </para>
        /// </summary>
        public static string DefaultPath(string flowId)
        {
            string id = (flowId ?? "").Trim();
            if (id.Length == 0) id = "default";

            // 流程标识来自流程文件，可能是中文/带空格；只清掉文件名非法字符
            char[] bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = true;
                for (int k = 0; k < bad.Length; k++) if (c == bad[k]) { ok = false; break; }
                sb.Append(ok ? c : '_');
            }

            return Path.Combine(Core.AppPaths.OcrModelDir, sb + Extension);
        }

        /// <summary>参数值 → 绝对路径（空值给默认；<c>Path.Combine</c> 遇绝对路径直接返回它）。</summary>
        public static string ResolvePath(string value, string flowId)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0) v = DefaultPath(flowId);
            try { return Path.IsPathRooted(v) ? v : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, v); }
            catch (Exception) { return v; }
        }

        /// <summary>
        /// 决定"这个节点该用哪个字符源"：
        /// 显式填了库文件 → 用它；没填但默认库文件已存在 → 用默认库；都没有 → 返回 null（走旧目录）。
        /// <para>
        /// 【为什么"显式填了就绝不回退"】库文件被删/改名时如果悄悄回退到旧目录，
        /// 现场会变成"训练好的库明明不在了，识别却还在出字"，排查时根本想不到去看目录。
        /// 宁可显示"库 0 个字符"，也要让缺失暴露出来。
        /// </para>
        /// </summary>
        public static string ResolveActive(string libValue, string flowId)
        {
            string v = (libValue ?? "").Trim();
            if (v.Length > 0) return ResolvePath(v, flowId);

            string def = DefaultPath(flowId);
            return File.Exists(def) ? def : null;
        }

        // ------------------------------------------------------------------
        //  读写
        // ------------------------------------------------------------------

        /// <summary>读库。文件不存在返回 <c>false</c> 且 <paramref name="missing"/> = true。</summary>
        public static bool Load(string path, out List<OcrCharSample> samples,
                                out bool missing, out string error)
        {
            samples = new List<OcrCharSample>();
            missing = false;
            error = null;

            if (string.IsNullOrWhiteSpace(path)) { error = "库文件路径为空"; return false; }
            if (!File.Exists(path)) { missing = true; error = "库文件不存在"; return false; }

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(fs, Encoding.UTF8))
                {
                    if (fs.Length < 12) { error = "文件太小，不是有效的字符库"; return false; }

                    string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
                    if (magic != Magic) { error = "文件头不匹配（不是 .ocrtpl 字符库）"; return false; }

                    int ver = br.ReadInt32();
                    if (ver != Version)
                    {
                        error = string.Format(CultureInfo.InvariantCulture,
                            "库文件版本 {0} 与当前程序（{1}）不一致，请升级程序或重建该库", ver, Version);
                        return false;
                    }

                    int count = br.ReadInt32();
                    if (count < 0 || count > MaxSamples)
                    {
                        error = string.Format(CultureInfo.InvariantCulture, "样本条数异常：{0}", count);
                        return false;
                    }

                    samples.Capacity = count;
                    for (int i = 0; i < count; i++)
                    {
                        int labelLen = br.ReadInt32();
                        if (labelLen < 0 || labelLen > 256) { error = "第 " + (i + 1) + " 条标签长度异常"; return false; }

                        string label = Encoding.UTF8.GetString(br.ReadBytes(labelLen));
                        int w = br.ReadInt32();
                        int h = br.ReadInt32();
                        if (w <= 0 || h <= 0 || w > MaxSide || h > MaxSide)
                        {
                            error = string.Format(CultureInfo.InvariantCulture,
                                "第 {0} 条样本尺寸异常：{1}×{2}", i + 1, w, h);
                            return false;
                        }

                        byte[] px = br.ReadBytes(w * h);
                        if (px.Length != w * h) { error = "第 " + (i + 1) + " 条样本像素不完整（文件被截断）"; return false; }

                        samples.Add(new OcrCharSample(label, w, h, px));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "读取失败：" + ex.Message;
                samples.Clear();
                return false;
            }
        }

        /// <summary>写库（整文件重写；先写临时文件再原子替换，避免写坏）。</summary>
        public static bool Save(string path, IList<OcrCharSample> samples, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path)) { error = "库文件路径为空"; return false; }
            if (samples == null) samples = new List<OcrCharSample>();
            if (samples.Count > MaxSamples)
            {
                error = "样本过多（" + samples.Count + "），上限 " + MaxSamples;
                return false;
            }

            string tmp = path + ".tmp";
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var bw = new BinaryWriter(fs, Encoding.UTF8))
                {
                    bw.Write(Encoding.ASCII.GetBytes(Magic));
                    bw.Write(Version);
                    bw.Write(samples.Count);

                    for (int i = 0; i < samples.Count; i++)
                    {
                        OcrCharSample s = samples[i];
                        byte[] label = Encoding.UTF8.GetBytes(s == null ? "" : (s.Label ?? ""));
                        bw.Write(label.Length);
                        bw.Write(label);
                        bw.Write(s.Width);
                        bw.Write(s.Height);
                        bw.Write(s.Pixels, 0, s.Width * s.Height);
                    }
                }

                if (File.Exists(path))
                {
                    // File.Replace 是原子替换：中途断电也不会留下半个库
                    File.Replace(tmp, path, null);
                }
                else
                {
                    File.Move(tmp, path);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "保存失败：" + ex.Message;
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
                return false;
            }
        }

        /// <summary>追加样本到库（不存在就新建）。返回追加后的总条数。</summary>
        public static bool Append(string path, IList<OcrCharSample> add, out int total, out string error)
        {
            total = 0;
            error = null;

            List<OcrCharSample> all;
            bool missing;
            if (!Load(path, out all, out missing, out error))
            {
                if (!missing) return false;      // 文件在但坏了：绝不覆盖（可能是重要的库）
                all = new List<OcrCharSample>();
            }

            if (add != null) all.AddRange(add);
            if (!Save(path, all, out error)) return false;

            total = all.Count;
            return true;
        }

        /// <summary>统计各标签的样本数（界面上显示"37 个字符 / 92 张样本"）。</summary>
        public static void CountByLabel(IList<OcrCharSample> samples, out int labels, out int total)
        {
            labels = 0;
            total = (samples == null) ? 0 : samples.Count;
            if (samples == null) return;

            var seen = new Dictionary<string, bool>(StringComparer.Ordinal);
            for (int i = 0; i < samples.Count; i++)
            {
                OcrCharSample s = samples[i];
                if (s == null) continue;
                if (!seen.ContainsKey(s.Label ?? "")) { seen[s.Label ?? ""] = true; labels++; }
            }
        }

        /// <summary>
        /// 按<strong>样本序号</strong>删除条目（整文件重写 + 原子替换）。
        /// </summary>
        /// <remarks>
        /// 【为什么按序号而不是按标签】一个字符通常有多份样本（不同角度/不同字体的同一个 "A"）。
        /// "删掉 A" 和 "删掉这一张不像 A 的样本" 是两件事，界面上要能只挑一张。
        /// 序号来自上一次 <see cref="Load"/> 的顺序，与界面清单一一对应。
        /// <para>
        /// 【坏文件绝不覆盖】<see cref="Load"/> 失败（文件在但内容坏了）时直接返回 false：
        /// 那种情况下的"删除"很可能把整个库写成一堆错位条目，不如让用户先看到错误。
        /// </para>
        /// </remarks>
        public static bool RemoveRange(string path, ICollection<int> indices,
                                       out int removed, out int total, out string error)
        {
            removed = 0;
            total = 0;
            error = null;

            if (indices == null || indices.Count == 0) { error = "没有选中要删除的样本"; return false; }
            if (string.IsNullOrWhiteSpace(path)) { error = "库文件路径为空"; return false; }

            List<OcrCharSample> all;
            bool missing;
            if (!Load(path, out all, out missing, out error))
            {
                if (missing) error = "库文件不存在，无需删除";
                return false;
            }

            var drop = new HashSet<int>();
            foreach (int i in indices)
                if (i >= 0 && i < all.Count) drop.Add(i);

            if (drop.Count == 0) { error = "选中的样本已不在库里（可能被另一个窗口改过），请刷新"; return false; }

            var keep = new List<OcrCharSample>(all.Count - drop.Count);
            for (int i = 0; i < all.Count; i++)
                if (!drop.Contains(i)) keep.Add(all[i]);

            if (!Save(path, keep, out error)) return false;

            removed = drop.Count;
            total = keep.Count;
            return true;
        }

        /// <summary>清空库内容（保留文件本身，条数归零）。</summary>
        public static bool Clear(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path)) { error = "库文件路径为空"; return false; }
            return Save(path, new List<OcrCharSample>(), out error);
        }

        // ------------------------------------------------------------------
        //  位图 ↔ 样本
        // ------------------------------------------------------------------

        /// <summary>
        /// 从位图裁一块做成样本（转 8bpp 灰度；彩色按 Rec.601 亮度）。
        /// </summary>
        public static OcrCharSample FromBitmap(Bitmap src, Rectangle rect, string label)
        {
            if (src == null) throw new ArgumentNullException("src");

            Rectangle used;
            byte[] px = ToGrayBytes(src, rect, out used);
            if (px == null || used.Width <= 0 || used.Height <= 0) return null;
            return new OcrCharSample(label, used.Width, used.Height, px);
        }

        /// <summary>整幅位图 → 样本。</summary>
        public static OcrCharSample FromBitmap(Bitmap src, string label)
        {
            if (src == null) throw new ArgumentNullException("src");
            return FromBitmap(src, new Rectangle(0, 0, src.Width, src.Height), label);
        }

        /// <summary>把样本还原成位图（预览/调试用）。</summary>
        public static Bitmap ToBitmap(OcrCharSample s)
        {
            if (s == null || s.Width <= 0 || s.Height <= 0) return null;

            var bmp = new Bitmap(s.Width, s.Height, PixelFormat.Format8bppIndexed);
            ColorPalette pal = bmp.Palette;
            for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
            bmp.Palette = pal;

            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, s.Width, s.Height),
                                         ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try
            {
                if (bd.Stride == s.Width)
                {
                    Marshal.Copy(s.Pixels, 0, bd.Scan0, s.Width * s.Height);
                }
                else
                {
                    for (int y = 0; y < s.Height; y++)
                    {
                        IntPtr dst = new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                        Marshal.Copy(s.Pixels, y * s.Width, dst, s.Width);
                    }
                }
            }
            finally { bmp.UnlockBits(bd); }

            return bmp;
        }

        /// <summary>裁一块并转 8bpp 灰度字节（无行尾填充）。</summary>
        private static byte[] ToGrayBytes(Bitmap src, Rectangle rect, out Rectangle used)
        {
            used = Rectangle.Intersect(rect, new Rectangle(0, 0, src.Width, src.Height));
            if (used.Width <= 0 || used.Height <= 0) return null;

            int w = used.Width, h = used.Height;

            // 统一先画到 24bpp：源图可能是 1/4/8/16/24/32bpp，逐个分支处理只会漏
            using (var tmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(tmp))
                {
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    g.DrawImage(src, new Rectangle(0, 0, w, h), used, GraphicsUnit.Pixel);
                }

                BitmapData bd = tmp.LockBits(new Rectangle(0, 0, w, h),
                                             ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    byte[] outPx = new byte[w * h];
                    byte[] row = new byte[bd.Stride];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row, 0, bd.Stride);
                        int o = y * w;
                        for (int x = 0; x < w; x++)
                        {
                            int b = row[x * 3];
                            int gg = row[x * 3 + 1];
                            int rr = row[x * 3 + 2];
                            // Rec.601：与原生库 AsGray 同一套系数，避免"界面看到的"和"算法吃到的"不一致
                            outPx[o + x] = (byte)((rr * 77 + gg * 150 + b * 29) >> 8);
                        }
                    }
                    return outPx;
                }
                finally { tmp.UnlockBits(bd); }
            }
        }
    }
}
