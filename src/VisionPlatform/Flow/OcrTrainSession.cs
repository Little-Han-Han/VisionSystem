using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

namespace VisionPlatform.Flow
{
    /// <summary>训练会话里的一个字符框（图像坐标 + 标签）。</summary>
    public sealed class OcrTrainBox
    {
        public int X;
        public int Y;
        public int W;
        public int H;
        public string Label = "?";
    }

    /// <summary>
    /// 「上次训练到哪了」的现场：画面 + 区域 + 每个字符框。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 【为什么不存进节点参数】字符框只是"这一轮还没入库的中间态"，一保存到库就作废了；
    /// 往 <c>FlowNode.Params</c> 里塞 N 个框会污染配置、还会被"每帧写回"刷爆。
    /// 存成库旁边的一个小文件，语义就是"这个库上次是在什么画面上训的"。
    /// </para>
    /// <para>
    /// 【存放位置】<c>&lt;库所在目录&gt;\session\&lt;库文件名&gt;.png | .txt</c>。
    /// 跟着库走：换台机器/换流程只要库在，接着训的就是同一张图。
    /// 放子目录而不是直接放在模型目录里，是为了不让 <c>OCRModel</c> 下多出一堆看不见后缀的杂物。
    /// </para>
    /// <para>
    /// 【格式】纯文本、制表符分隔（标签是单个"文本元素"，不可能含制表符，因此不需要转义）：
    /// <code>
    ///   1                      ← 版本
    ///   640	480                ← 画面尺寸
    ///   120	80	400	60         ← 区域（0 0 0 0 = 整图）
    ///   10	12	18	24	A        ← 每个字符框一行
    /// </code>
    /// 选文本而不是二进制/JSON：出问题时能用记事本打开看，改坏了也只是丢一次现场。
    /// </para>
    /// </remarks>
    public sealed class OcrTrainSession
    {
        private const int Version = 1;

        /// <summary>训练区域（图像坐标）；空 = 整图。</summary>
        public Rectangle Roi = Rectangle.Empty;

        /// <summary>画面尺寸（用于判断这份现场还配不配得上当前库/画面）。</summary>
        public int ImageWidth;
        public int ImageHeight;

        /// <summary>字符框（按阅读顺序）。</summary>
        public readonly List<OcrTrainBox> Boxes = new List<OcrTrainBox>();

        // ------------------------------------------------------------------
        //  路径
        // ------------------------------------------------------------------

        private static string DirOf(string libPath)
        {
            string dir = Path.GetDirectoryName(libPath);
            if (string.IsNullOrEmpty(dir)) dir = ".";
            return Path.Combine(dir, "session");
        }

        private static string BaseOf(string libPath)
        {
            string name = Path.GetFileNameWithoutExtension(libPath);
            if (string.IsNullOrEmpty(name)) name = "default";
            return name;
        }

        /// <summary>会话图片路径（PNG）。</summary>
        public static string ImagePathOf(string libPath)
        {
            return Path.Combine(DirOf(libPath), BaseOf(libPath) + ".png");
        }

        /// <summary>会话状态路径（文本）。</summary>
        public static string StatePathOf(string libPath)
        {
            return Path.Combine(DirOf(libPath), BaseOf(libPath) + ".txt");
        }

        /// <summary>是否已经存过现场（判"能不能恢复"而不必真读）。</summary>
        public static bool Exists(string libPath)
        {
            if (string.IsNullOrWhiteSpace(libPath)) return false;
            try { return File.Exists(StatePathOf(libPath)) && File.Exists(ImagePathOf(libPath)); }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------------
        //  保存 / 读取
        // ------------------------------------------------------------------

        /// <summary>
        /// 保存现场。<strong>任何失败都只返回 false，不抛</strong> ——
        /// 关窗时写盘失败绝不能变成"关不掉"或弹框打断人。
        /// </summary>
        public static bool Save(string libPath, Bitmap frame, Rectangle roi,
                                IList<OcrTrainBox> boxes, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(libPath)) { error = "库文件路径为空"; return false; }
            if (frame == null) { error = "没有画面"; return false; }

            string dir = DirOf(libPath);
            string png = ImagePathOf(libPath);
            string txt = StatePathOf(libPath);
            string pngTmp = png + ".tmp";
            string txtTmp = txt + ".tmp";

            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 先写图片：图片没落地就不要写状态，否则下次会恢复出"有框没图"
                frame.Save(pngTmp, ImageFormat.Png);
                if (File.Exists(png)) File.Delete(png);
                File.Move(pngTmp, png);

                var sb = new StringBuilder();
                sb.AppendLine(Version.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(frame.Width.ToString(CultureInfo.InvariantCulture) + "\t"
                            + frame.Height.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(roi.Width > 0 && roi.Height > 0
                    ? string.Format(CultureInfo.InvariantCulture, "{0}\t{1}\t{2}\t{3}", roi.X, roi.Y, roi.Width, roi.Height)
                    : "0\t0\t0\t0");

                if (boxes != null)
                {
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        OcrTrainBox b = boxes[i];
                        if (b == null || b.W <= 0 || b.H <= 0) continue;
                        sb.Append(b.X.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(b.Y.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(b.W.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append(b.H.ToString(CultureInfo.InvariantCulture)).Append('\t')
                          .Append((b.Label ?? "?").Replace("\t", " ")).AppendLine();
                    }
                }

                File.WriteAllText(txtTmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(txt)) File.Delete(txt);
                File.Move(txtTmp, txt);
                return true;
            }
            catch (Exception ex)
            {
                error = "保存训练现场失败：" + ex.Message;
                try { if (File.Exists(pngTmp)) File.Delete(pngTmp); } catch (Exception) { }
                try { if (File.Exists(txtTmp)) File.Delete(txtTmp); } catch (Exception) { }
                return false;
            }
        }

        /// <summary>
        /// 读取现场。<paramref name="frame"/> 由调用方持有（失败时为 null）。
        /// 文件不存在属于正常情况（第一次训练），返回 false 且 <paramref name="missing"/> = true。
        /// </summary>
        public static bool Load(string libPath, out OcrTrainSession session, out Bitmap frame,
                                out bool missing, out string error)
        {
            session = null;
            frame = null;
            missing = false;
            error = null;

            if (string.IsNullOrWhiteSpace(libPath)) { missing = true; return false; }

            string png = ImagePathOf(libPath);
            string txt = StatePathOf(libPath);

            try
            {
                if (!File.Exists(txt) || !File.Exists(png)) { missing = true; return false; }

                string[] lines = File.ReadAllLines(txt, Encoding.UTF8);
                if (lines.Length < 3) { error = "训练现场文件不完整"; return false; }

                int ver;
                if (!int.TryParse(lines[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ver) || ver != Version)
                {
                    error = "训练现场版本不匹配（" + lines[0].Trim() + "）";
                    return false;
                }

                int w, h;
                if (!TryParsePair(lines[1], out w, out h) || w <= 0 || h <= 0) { error = "训练现场的画面尺寸无效"; return false; }

                var s = new OcrTrainSession { ImageWidth = w, ImageHeight = h };

                int rx, ry, rw, rh;
                if (TryParseQuad(lines[2], out rx, out ry, out rw, out rh) && rw > 0 && rh > 0)
                    s.Roi = new Rectangle(rx, ry, rw, rh);

                for (int i = 3; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line)) continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length < 5) continue;

                    int bx, by, bw, bh;
                    if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out bx)) continue;
                    if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out by)) continue;
                    if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out bw)) continue;
                    if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out bh)) continue;
                    if (bw <= 0 || bh <= 0) continue;

                    s.Boxes.Add(new OcrTrainBox { X = bx, Y = by, W = bw, H = bh, Label = parts[4] });
                }

                // 图片最后读：状态坏了就不必白读一张大图
                var bmp = new Bitmap(png);
                if (bmp.Width != w || bmp.Height != h)
                {
                    // 图片与状态不是同一次写的（极少数：写完图没写完状态就断电）。以图片为准丢弃状态。
                    bmp.Dispose();
                    error = "训练现场的画面与状态不一致，已忽略";
                    return false;
                }

                session = s;
                frame = bmp;
                return true;
            }
            catch (Exception ex)
            {
                error = "读取训练现场失败：" + ex.Message;
                if (frame != null) { frame.Dispose(); frame = null; }
                session = null;
                return false;
            }
        }

        private static bool TryParsePair(string line, out int a, out int b)
        {
            a = b = 0;
            string[] p = (line ?? "").Split('\t');
            if (p.Length < 2) return false;
            return int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out b);
        }

        private static bool TryParseQuad(string line, out int a, out int b, out int c, out int d)
        {
            a = b = c = d = 0;
            string[] p = (line ?? "").Split('\t');
            if (p.Length < 4) return false;
            return int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out b)
                && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out c)
                && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out d);
        }
    }
}
