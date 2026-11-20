using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace VisionSystem.Controls.PointCloud
{
    /// <summary>
    /// 点云文件读取器：PLY（ascii / binary_little_endian）、PCD（ascii / binary）、深度图（16bit PNG / 8bit 常规格式）。
    /// <para>
    /// 【为什么自写解析器】原生算法库没有任何点云接口，也不想引入 PCL/第三方 DLL ——
    /// PLY/PCD 的「顶点子集」（x y z + rgb）解析在 C# 里就一两百行；16bit PNG 深度图
    /// GDI+ 会压成 8bit，必须自己解 IHDR/IDAT 才能保住 65535 级深度。
    /// </para>
    /// </summary>
    public static class PointCloudFileReader
    {
        /// <summary>支持的扩展名（小写，含点）。</summary>
        public static readonly string[] Extensions = { ".ply", ".pcd", ".png", ".bmp", ".jpg", ".jpeg", ".tif", ".tiff" };

        /// <summary>按扩展名加载：ply/pcd 走点云解析；图片扩展名走深度图（内参反投影）。</summary>
        public static PointCloudData Load(string path, double fx, double fy, double cx, double cy, double zScale, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = "点云文件不存在：" + path;
                return null;
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (ext == ".ply") return LoadPly(path, out error);
                if (ext == ".pcd") return LoadPcd(path, out error);
                return LoadDepthImage(path, fx, fy, cx, cy, zScale, out error);
            }
            catch (Exception ex)
            {
                error = "读取点云失败：" + ex.Message;
                return null;
            }
        }

        // =====================================================================
        //  PLY
        // =====================================================================

        public static PointCloudData LoadPly(string path, out string error)
        {
            error = null;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                // ---- 头部：逐行读到 end_header ----
                string format = null;
                int vertexCount = 0;
                var props = new List<KeyValuePair<string, string>>();   // 名称 -> 类型
                bool inVertex = false;
                long headerEndOffset = -1;

                while (true)
                {
                    string line = ReadAsciiLine(br);
                    if (line == null) { error = "PLY 头部不完整（没读到 end_header）"; return null; }
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    string[] tok = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tok.Length == 0) continue;

                    string kw = tok[0].ToLowerInvariant();
                    if (kw == "format")
                    {
                        if (tok.Length >= 2) format = tok[1].ToLowerInvariant();
                    }
                    else if (kw == "element")
                    {
                        // 只认 vertex 元素（面/边等跳过）
                        inVertex = tok.Length >= 2 && tok[1].ToLowerInvariant() == "vertex";
                        if (inVertex && tok.Length >= 3)
                            int.TryParse(tok[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out vertexCount);
                    }
                    else if (kw == "property" && inVertex && tok.Length >= 3)
                    {
                        props.Add(new KeyValuePair<string, string>(tok[2].ToLowerInvariant(), tok[1].ToLowerInvariant()));
                    }
                    else if (kw == "end_header")
                    {
                        headerEndOffset = fs.Position;
                        break;
                    }
                }

                if (format == null || headerEndOffset < 0) { error = "PLY 头部缺少 format/end_header"; return null; }
                if (vertexCount <= 0) { error = "PLY 没有顶点（element vertex = 0）"; return null; }

                // ---- 顶点属性定位：x/y/z 必须有，rgb 可选 ----
                int ix = -1, iy = -1, iz = -1, ir = -1, ig = -1, ib = -1;
                for (int i = 0; i < props.Count; i++)
                {
                    string name = props[i].Key;
                    if (ix < 0 && name == "x") ix = i;
                    else if (iy < 0 && name == "y") iy = i;
                    else if (iz < 0 && name == "z") iz = i;
                    else if (ir < 0 && (name == "red" || name == "r")) ir = i;
                    else if (ig < 0 && (name == "green" || name == "g")) ig = i;
                    else if (ib < 0 && (name == "blue" || name == "b")) ib = i;
                }
                if (ix < 0 || iy < 0 || iz < 0) { error = "PLY 顶点缺少 x/y/z 属性"; return null; }

                bool hasColor = ir >= 0 && ig >= 0 && ib >= 0;
                bool isAscii = format.StartsWith("ascii", StringComparison.Ordinal);
                bool bigEndian = format.IndexOf("big", StringComparison.Ordinal) >= 0;

                float[] xyz = new float[vertexCount * 3];
                byte[] rgb = hasColor ? new byte[vertexCount * 3] : null;

                if (isAscii)
                {
                    // ascii 头部之后可能直接跟数据（也可能有回车差异），从 offset 起按行切
                    using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
                    {
                        reader.BaseStream.Seek(headerEndOffset, SeekOrigin.Begin);
                        int read = 0;
                        while (read < vertexCount)
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;
                            line = line.Trim();
                            if (line.Length == 0) continue;

                            string[] v = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (v.Length < props.Count) continue;

                            xyz[read * 3] = ParseF(v[ix]);
                            xyz[read * 3 + 1] = ParseF(v[iy]);
                            xyz[read * 3 + 2] = ParseF(v[iz]);
                            if (hasColor)
                            {
                                rgb[read * 3] = (byte)ClampByte(ParseF(v[ir]));
                                rgb[read * 3 + 1] = (byte)ClampByte(ParseF(v[ig]));
                                rgb[read * 3 + 2] = (byte)ClampByte(ParseF(v[ib]));
                            }
                            read++;
                        }
                        if (read < vertexCount) { error = "PLY ascii 顶点不足：读到 " + read + "/" + vertexCount; return null; }
                    }
                }
                else
                {
                    fs.Seek(headerEndOffset, SeekOrigin.Begin);
                    // 每个属性的字节宽（float/int=4, double=8, uchar/char=1, short/ushort=2）
                    int stride = 0;
                    var sizes = new int[props.Count];
                    for (int i = 0; i < props.Count; i++)
                    {
                        sizes[i] = TypeSize(props[i].Value);
                        stride += sizes[i];
                    }
                    if (stride <= 0) { error = "PLY 属性类型无法识别"; return null; }

                    byte[] buf = br.ReadBytes(stride * vertexCount);
                    if (buf.Length < stride * vertexCount) { error = "PLY binary 数据不足"; return null; }

                    for (int p = 0; p < vertexCount; p++)
                    {
                        int baseOff = p * stride;
                        xyz[p * 3] = ReadProp(buf, baseOff, props, ix, sizes, bigEndian);
                        xyz[p * 3 + 1] = ReadProp(buf, baseOff, props, iy, sizes, bigEndian);
                        xyz[p * 3 + 2] = ReadProp(buf, baseOff, props, iz, sizes, bigEndian);
                        if (hasColor)
                        {
                            rgb[p * 3] = (byte)ClampByte(ReadProp(buf, baseOff, props, ir, sizes, bigEndian));
                            rgb[p * 3 + 1] = (byte)ClampByte(ReadProp(buf, baseOff, props, ig, sizes, bigEndian));
                            rgb[p * 3 + 2] = (byte)ClampByte(ReadProp(buf, baseOff, props, ib, sizes, bigEndian));
                        }
                    }
                }

                return new PointCloudData(xyz, vertexCount, rgb);
            }
        }

        private static float ReadProp(byte[] buf, int pointBase, List<KeyValuePair<string, string>> props,
                                      int propIndex, int[] sizes, bool bigEndian)
        {
            int off = pointBase;
            for (int i = 0; i < propIndex; i++) off += sizes[i];

            string type = props[propIndex].Value;
            if (type.StartsWith("float", StringComparison.Ordinal) || type == "float32")
                return BitConverter.ToSingle(buf, off);
            if (type == "float64" || type == "double")
                return (float)BitConverter.ToDouble(buf, off);
            if (type == "int" || type == "int32")
                return BitConverter.ToInt32(buf, off);
            if (type == "uint" || type == "uint32")
                return BitConverter.ToUInt32(buf, off);
            if (type == "short" || type == "int16")
                return BitConverter.ToInt16(buf, off);
            if (type == "ushort" || type == "uint16")
                return BitConverter.ToUInt16(buf, off);
            if (type == "char" || type == "int8" || type == "uchar" || type == "uint8")
                return buf[off];
            return 0f;
        }

        private static int TypeSize(string plyType)
        {
            switch (plyType)
            {
                case "char": case "int8":
                case "uchar": case "uint8":
                    return 1;
                case "short": case "int16":
                case "ushort": case "uint16":
                    return 2;
                case "int": case "int32":
                case "uint": case "uint32":
                case "float": case "float32":
                    return 4;
                case "double": case "float64":
                    return 8;
                default:
                    return 0;
            }
        }

        // =====================================================================
        //  PCD
        // =====================================================================

        public static PointCloudData LoadPcd(string path, out string error)
        {
            error = null;
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                string dataMode = null;
                var fields = new List<string>();
                int pointCount = 0;
                long binaryDataOffset = -1;

                while (true)
                {
                    string line = ReadAsciiLine(br);
                    if (line == null) { error = "PCD 头部不完整（没读到 DATA）"; return null; }
                    line = line.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                    string[] tok = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tok.Length == 0) continue;

                    string kw = tok[0].ToUpperInvariant();
                    if (kw == "FIELDS")
                    {
                        fields.Clear();
                        for (int i = 1; i < tok.Length; i++) fields.Add(tok[i].ToLowerInvariant());
                    }
                    else if (kw == "POINTS" && tok.Length >= 2)
                    {
                        int.TryParse(tok[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out pointCount);
                    }
                    else if (kw == "DATA" && tok.Length >= 2)
                    {
                        dataMode = tok[1].ToLowerInvariant();
                        binaryDataOffset = fs.Position;
                        break;
                    }
                }

                if (dataMode == null) { error = "PCD 缺少 DATA 行"; return null; }
                if (pointCount <= 0) { error = "PCD 点数为 0"; return null; }

                int ix = fields.IndexOf("x"), iy = fields.IndexOf("y"), iz = fields.IndexOf("z");
                int irgb = fields.IndexOf("rgb");
                if (irgb < 0) irgb = fields.IndexOf("rgba");
                if (ix < 0 || iy < 0 || iz < 0) { error = "PCD FIELDS 缺少 x/y/z"; return null; }

                float[] xyz = new float[pointCount * 3];
                byte[] rgb = irgb >= 0 ? new byte[pointCount * 3] : null;

                if (dataMode == "ascii")
                {
                    using (var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
                    {
                        reader.BaseStream.Seek(binaryDataOffset, SeekOrigin.Begin);
                        int read = 0;
                        while (read < pointCount)
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;
                            line = line.Trim();
                            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                            string[] v = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (v.Length < fields.Count) continue;

                            xyz[read * 3] = ParseF(v[ix]);
                            xyz[read * 3 + 1] = ParseF(v[iy]);
                            xyz[read * 3 + 2] = ParseF(v[iz]);
                            if (rgb != null) UnpackPcdRgb(ParseF(v[irgb]), rgb, read * 3);
                            read++;
                        }
                        if (read < pointCount) { error = "PCD ascii 点数不足：读到 " + read + "/" + pointCount; return null; }
                    }
                }
                else if (dataMode == "binary")
                {
                    fs.Seek(binaryDataOffset, SeekOrigin.Begin);
                    int stride = fields.Count * 4;      // v1 只支持 SIZE 全为 float32 的常规导出
                    byte[] buf = br.ReadBytes(stride * pointCount);
                    if (buf.Length < stride * pointCount) { error = "PCD binary 数据不足"; return null; }

                    for (int p = 0; p < pointCount; p++)
                    {
                        int baseOff = p * stride;
                        xyz[p * 3] = BitConverter.ToSingle(buf, baseOff + ix * 4);
                        xyz[p * 3 + 1] = BitConverter.ToSingle(buf, baseOff + iy * 4);
                        xyz[p * 3 + 2] = BitConverter.ToSingle(buf, baseOff + iz * 4);
                        if (rgb != null) UnpackPcdRgb(BitConverter.ToSingle(buf, baseOff + irgb * 4), rgb, p * 3);
                    }
                }
                else
                {
                    error = "PCD 的 DATA=" + dataMode + " 暂不支持（支持 ascii / binary）";
                    return null;
                }

                return new PointCloudData(xyz, pointCount, rgb);
            }
        }

        /// <summary>PCD 的 rgb 字段是打包成 float 的 32 位整数（0x00RRGGBB）。</summary>
        private static void UnpackPcdRgb(float packed, byte[] rgb, int offset)
        {
            uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(packed), 0);
            rgb[offset] = (byte)((bits >> 16) & 0xFF);
            rgb[offset + 1] = (byte)((bits >> 8) & 0xFF);
            rgb[offset + 2] = (byte)(bits & 0xFF);
        }

        // =====================================================================
        //  深度图 -> 点云（内参反投影）
        // =====================================================================

        /// <summary>
        /// 深度图转点云：X = (u-cx)*Z/Fx，Y = (v-cy)*Z/Fy，Z = 深度 * zScale。
        /// 深度值 0（无效点）剔除。16bit PNG 走自研解析（GDI+ 会把 16bit 压成 8bit）。
        /// </summary>
        public static PointCloudData LoadDepthImage(string path, double fx, double fy, double cx, double cy,
                                                    double zScale, out string error)
        {
            error = null;
            ushort[] depth = null;
            int width = 0, height = 0, maxRaw = 0;
            bool is16 = TryReadPng16Gray(path, out depth, out width, out height, out maxRaw);

            if (!is16)
            {
                // 常规 8bit 图片（bmp/jpg/8bit png）：GDI+ 解码，灰度值 * zScale
                Bitmap bmp = null;
                try
                {
                    using (var probe = Image.FromFile(path))
                        bmp = new Bitmap(probe);
                }
                catch (Exception ex)
                {
                    error = "深度图解码失败：" + ex.Message;
                    return null;
                }

                using (bmp)
                {
                    width = bmp.Width;
                    height = bmp.Height;
                    var rect = new Rectangle(0, 0, width, height);
                    var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                    try
                    {
                        var gray = new ushort[width * height];
                    byte[] row = new byte[width * 3];
                    IntPtr p = data.Scan0;
                    for (int y = 0; y < height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(p, row, 0, width * 3);
                        for (int x = 0; x < width; x++)
                            gray[y * width + x] = row[x * 3];       // 取 B（灰度图 B=G=R）
                        p = (IntPtr)((long)p + data.Stride);
                    }
                    maxRaw = 255;
                    return BuildFromDepth(gray, width, height, fx, fy, cx, cy, zScale, out error);
                    }
                    finally
                    {
                        bmp.UnlockBits(data);
                    }
                }
            }

            return BuildFromDepth(depth, width, height, fx, fy, cx, cy, zScale, out error);
        }

        private static PointCloudData BuildFromDepth(ushort[] depth, int width, int height,
                                                     double fx, double fy, double cx, double cy,
                                                     double zScale, out string error)
        {
            error = null;
            if (fx <= 0 || fy <= 0)
            {
                error = "深度图转点云需要有效内参（Fx/Fy 必须大于 0）";
                return null;
            }

            var pts = new List<float>(depth.Length * 3);
            for (int v = 0; v < height; v++)
            {
                for (int u = 0; u < width; u++)
                {
                    float z = depth[v * width + u] * (float)zScale;
                    if (z <= 0f) continue;                          // 0 = 无效深度
                    pts.Add((float)((u - cx) * z / fx));
                    pts.Add((float)((v - cy) * z / fy));
                    pts.Add(z);
                }
            }

            if (pts.Count == 0) { error = "深度图没有有效深度点（全为 0？）"; return null; }
            return new PointCloudData(pts.ToArray(), pts.Count / 3, null);
        }

        /// <summary>
        /// 最小 16bit 灰度 PNG 解析（仅 IHDR/IDAT，color type 0，bit depth 16；8bit 及其它格式返回 false 走 GDI+）。
        /// 手写的原因：GDI+ 解 16bit PNG 会截断成 8bit，深度分辨率直接掉 256 倍。
        /// </summary>
        private static bool TryReadPng16Gray(string path, out ushort[] depth, out int width, out int height, out int maxRaw)
        {
            depth = null; width = 0; height = 0; maxRaw = 0;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length < 57) return false;
                if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) return false;

                int pos = 8;
                int w = 0, h = 0, bitDepth = 0, colorType = 0;
                var idat = new List<byte[]>();

                while (pos + 8 <= bytes.Length)
                {
                    int len = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
                    string type = Encoding.ASCII.GetString(bytes, pos + 4, 4);
                    int dataStart = pos + 8;
                    if (dataStart + len > bytes.Length) return false;

                    if (type == "IHDR")
                    {
                        w = (bytes[dataStart] << 24) | (bytes[dataStart + 1] << 16) | (bytes[dataStart + 2] << 8) | bytes[dataStart + 3];
                        h = (bytes[dataStart + 4] << 24) | (bytes[dataStart + 5] << 16) | (bytes[dataStart + 6] << 8) | bytes[dataStart + 7];
                        bitDepth = bytes[dataStart + 8];
                        colorType = bytes[dataStart + 9];
                        int interlace = bytes[dataStart + 12];
                        if (interlace != 0) return false;           // 隔行 PNG 不支持
                    }
                    else if (type == "IDAT")
                    {
                        var chunk = new byte[len];
                        Array.Copy(bytes, dataStart, chunk, 0, len);
                        idat.Add(chunk);
                    }
                    else if (type == "IEND") break;

                    pos = dataStart + len + 4;                      // 跳过 CRC
                }

                if (w <= 0 || h <= 0 || idat.Count == 0) return false;
                if (colorType != 0) return false;                   // 只做灰度
                if (bitDepth != 16) return false;                   // 只补 GDI+ 做不了的 16bit

                // zlib：跳过 2 字节头，直接 raw inflate
                var zlibStream = new MemoryStream();
                foreach (byte[] chunk in idat) zlibStream.Write(chunk, 0, chunk.Length);
                zlibStream.Position = 2;
                byte[] raw;
                using (var inflater = new DeflateStream(zlibStream, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    inflater.CopyTo(output);
                    raw = output.ToArray();
                }

                int bpp = 2;                                        // 16bit 灰度每像素 2 字节
                int stride = w * bpp;
                if (raw.Length < (stride + 1) * h) return false;    // 每行 1 字节 filter 标记

                depth = new ushort[w * h];
                var prev = new byte[stride];
                maxRaw = 0;

                for (int y = 0; y < h; y++)
                {
                    int rowStart = y * (stride + 1);
                    int filter = raw[rowStart];
                    byte[] cur = new byte[stride];
                    Array.Copy(raw, rowStart + 1, cur, 0, stride);
                    UnfilterRow(cur, prev, filter, bpp);

                    for (int x = 0; x < w; x++)
                    {
                        ushort v = (ushort)((cur[x * 2] << 8) | cur[x * 2 + 1]);    // 大端
                        depth[y * w + x] = v;
                        if (v > maxRaw) maxRaw = v;
                    }
                    Array.Copy(cur, prev, stride);
                }

                width = w;
                height = h;
                return true;
            }
            catch (Exception)
            {
                return false;                                       // 不是 PNG / 解不开 -> 走 GDI+
            }
        }

        private static void UnfilterRow(byte[] cur, byte[] prev, int filter, int bpp)
        {
            switch (filter)
            {
                case 1:     // Sub
                    for (int i = bpp; i < cur.Length; i++) cur[i] = (byte)(cur[i] + cur[i - bpp]);
                    break;
                case 2:     // Up
                    for (int i = 0; i < cur.Length; i++) cur[i] = (byte)(cur[i] + prev[i]);
                    break;
                case 3:     // Average
                    for (int i = 0; i < cur.Length; i++)
                    {
                        int left = i >= bpp ? cur[i - bpp] : 0;
                        cur[i] = (byte)(cur[i] + ((left + prev[i]) >> 1));
                    }
                    break;
                case 4:     // Paeth
                    for (int i = 0; i < cur.Length; i++)
                    {
                        int left = i >= bpp ? cur[i - bpp] : 0;
                        int up = prev[i];
                        int upleft = i >= bpp ? prev[i - bpp] : 0;
                        cur[i] = (byte)(cur[i] + Paeth(left, up, upleft));
                    }
                    break;
                default:    // 0 = None
                    break;
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            if (pb <= pc) return b;
            return c;
        }

        // =====================================================================
        //  公共小工具
        // =====================================================================

        private static string ReadAsciiLine(BinaryReader br)
        {
            var sb = new StringBuilder();
            try
            {
                while (true)
                {
                    int c = br.Read();
                    if (c < 0) return sb.Length == 0 ? null : sb.ToString();
                    if (c == '\n') return sb.ToString();
                    if (c != '\r') sb.Append((char)c);
                    if (sb.Length > 4096) return sb.ToString();     // 头部行不会这么长，防脏数据
                }
            }
            catch (EndOfStreamException)
            {
                return sb.Length == 0 ? null : sb.ToString();
            }
        }

        private static float ParseF(string s)
        {
            float v;
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                v = 0f;
            return v;
        }

        private static float ClampByte(float v)
        {
            if (v < 0f) return 0f;
            if (v > 255f) return 255f;
            return v;
        }
    }
}
