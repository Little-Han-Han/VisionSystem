using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using VisionPlatform.Core;
using VisionPlatform.Native;

namespace VisionPlatform.Flow
{
    /// <summary>模板资产加载失败的原因分类（三类文案必须不同 —— PRD 6.3 的硬要求）。</summary>
    public enum TemplateLoadError
    {
        /// <summary>没有失败。</summary>
        None = 0,

        /// <summary>文件不存在：还没做过模板，或模板已被删除。</summary>
        FileMissing = 1,

        /// <summary>文件损坏、无法解码，或 <c>SchemaVersion</c> 不兼容。</summary>
        CorruptedOrVersionMismatch = 2,

        /// <summary>模板资产与当前节点的设置不匹配（ROI 被改过 / 算法类型不同）。</summary>
        MismatchWithNode = 3
    }

    /// <summary>
    /// 模板元数据（<c>template.json</c> 的全部内容）。
    /// <para>
    /// 为什么把训练参数也存进来：运行期"参数被改 → 用已保存的模板图重训"需要知道当时用的参数；
    /// 而"匹配参数被改 → 不重训"需要能区分训练参数与匹配参数，故 <see cref="TrainSignature"/>
    /// 只记录训练三参数，不记录角度 / 缩放 / 金字塔。
    /// </para>
    /// </summary>
    [DataContract]
    public sealed class TemplateMeta
    {
        /// <summary>元数据结构版本；当前只认 1。</summary>
        [DataMember] public int SchemaVersion { get; set; }

        /// <summary>算法类型：<c>"ShapeMatch"</c> 或 <c>"TemplateMatch"</c>。</summary>
        [DataMember] public string Algorithm { get; set; }

        /// <summary>创建时的程序版本（诊断用）。</summary>
        [DataMember] public string AppVersion { get; set; }

        /// <summary>创建时间（ISO 8601，UTC）。</summary>
        [DataMember] public string CreatedUtc { get; set; }

        /// <summary>源帧宽度（记录训练帧尺寸，便于排查"换相机后模板变小"）。</summary>
        [DataMember] public int SourceFrameWidth { get; set; }

        /// <summary>源帧高度。</summary>
        [DataMember] public int SourceFrameHeight { get; set; }

        /// <summary>模板 ROI 的 X（源帧坐标；0 宽高表示整帧）。</summary>
        [DataMember] public int TemplateX { get; set; }

        /// <summary>模板 ROI 的 Y。</summary>
        [DataMember] public int TemplateY { get; set; }

        /// <summary>模板 ROI 的宽。</summary>
        [DataMember] public int TemplateW { get; set; }

        /// <summary>模板 ROI 的高。</summary>
        [DataMember] public int TemplateH { get; set; }

        /// <summary>模板 ROI 的旋转角（预留，当前恒 0）。</summary>
        [DataMember] public double TemplateAngle { get; set; }

        /// <summary>形状匹配训练参数：对比度。</summary>
        [DataMember] public double Contrast { get; set; }

        /// <summary>形状匹配训练参数：最多特征点。</summary>
        [DataMember] public int MaxFeatures { get; set; }

        /// <summary>形状匹配训练参数：最小特征间距。</summary>
        [DataMember] public double MinFeatureDist { get; set; }

        /// <summary>
        /// 训练参数签名（<c>"C=30|F=1500|D=2"</c>）。
        /// <strong>只含训练参数、不含 ROI</strong> —— 这是切断"上游位置修正 ±1px 抖动导致每帧重训"的关键。
        /// </summary>
        [DataMember] public string TrainSignature { get; set; }

        /// <summary>特征点数；NCC 恒 0。</summary>
        [DataMember] public int FeatureCount { get; set; }

        /// <summary>训练耗时（毫秒）。</summary>
        [DataMember] public int TrainElapsedMs { get; set; }

        /// <summary>模板图文件名（当前恒 <c>template.png</c>）。</summary>
        [DataMember] public string ImageFile { get; set; }

        /// <summary>模板图宽度（= 裁剪出的模板像素宽）。</summary>
        [DataMember] public int ImageW { get; set; }

        /// <summary>模板图高度。</summary>
        [DataMember] public int ImageH { get; set; }

        /// <summary>模板图像素格式名（<c>8bppIndexed</c> / <c>24bppRgb</c>）。</summary>
        [DataMember] public string ImagePixelFormat { get; set; }

        /// <summary>模板 PNG 的 SHA-256（回读校验与"外部覆盖"检测用）。</summary>
        [DataMember] public string ImageSha256 { get; set; }

        /// <summary>创建一份带默认值的元数据（避免反序列化缺字段后出现 0 值误判）。</summary>
        public static TemplateMeta CreateDefault()
        {
            return new TemplateMeta
            {
                SchemaVersion = TemplateStore.SchemaVersion,
                Algorithm = "ShapeMatch",
                AppVersion = "",
                CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                SourceFrameWidth = 0,
                SourceFrameHeight = 0,
                TemplateX = 0,
                TemplateY = 0,
                TemplateW = 0,
                TemplateH = 0,
                TemplateAngle = 0.0,
                Contrast = 30.0,
                MaxFeatures = 1500,
                MinFeatureDist = 2.0,
                TrainSignature = "",
                FeatureCount = 0,
                TrainElapsedMs = 0,
                ImageFile = TemplateStore.ImageFileName,
                ImageW = 0,
                ImageH = 0,
                ImagePixelFormat = "",
                ImageSha256 = ""
            };
        }
    }

    /// <summary>一份已加载的模板资产。<see cref="Template"/> 由调用方负责 Dispose。</summary>
    public sealed class TemplateAsset
    {
        /// <summary>元数据。</summary>
        public TemplateMeta Meta;

        /// <summary>模板图（已从磁盘复制出来，不持有文件句柄）。</summary>
        public Bitmap Template;

        /// <summary>资产所在目录。</summary>
        public string Dir;

        /// <summary>模板戳（json + png 的 mtime + length）；缓存失效的唯一依据。</summary>
        public string Stamp;
    }

    /// <summary>
    /// 模板资产的落盘与读取。
    /// <para>
    /// <strong>存在什么</strong>：训练那一帧的模板 ROI 裁剪副本（<c>template.png</c>，无损）
    /// + 元数据（<c>template.json</c>）。存裁剪副本而不是整幅原图，是因为运行期重训只需要那一小块，
    /// 整幅图既占空间又会被后续帧污染（"示教帧"的意义就没了）。
    /// </para>
    /// <para>
    /// <strong>缓存失效靠什么</strong>：只有 <see cref="StampOf"/>（json + png 的 mtime + length）。
    /// 删除 / 重做 / 外部拷贝覆盖三条路径都因此自动生效，<strong>不引入任何跨对象通知机制</strong>。
    /// </para>
    /// </summary>
    public static class TemplateStore
    {
        /// <summary>当前元数据结构版本。读到别的版本一律按"不兼容"处理，不尝试猜测。</summary>
        public const int SchemaVersion = 1;

        /// <summary>模板图文件名。</summary>
        public const string ImageFileName = "template.png";

        /// <summary>元数据文件名。</summary>
        public const string MetaFileName = "template.json";

        /// <summary>单个模板 PNG 的体积上限（超过则先转灰度、仍超则拒绝保存）。</summary>
        public const long MaxPngBytes = 2L * 1024L * 1024L;

        // =====================================================================
        //  路径
        // =====================================================================

        /// <summary>某个节点模板资产的目录（不存在也会返回路径）。</summary>
        public static string DirOf(string flowId, string nodeId)
        {
            return Path.Combine(AppPaths.TemplateDir, SanitizeId(flowId), SanitizeId(nodeId));
        }

        /// <summary>模板图路径。</summary>
        public static string PngPathOf(string flowId, string nodeId)
        {
            return Path.Combine(DirOf(flowId, nodeId), ImageFileName);
        }

        /// <summary>元数据路径。</summary>
        public static string MetaPathOf(string flowId, string nodeId)
        {
            return Path.Combine(DirOf(flowId, nodeId), MetaFileName);
        }

        /// <summary>模板资产是否存在（要求两个文件都在）。</summary>
        public static bool Exists(string flowId, string nodeId)
        {
            if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(nodeId)) return false;
            return File.Exists(PngPathOf(flowId, nodeId)) && File.Exists(MetaPathOf(flowId, nodeId));
        }

        /// <summary>
        /// 模板戳：<c>"json长度@ticks|png长度@ticks"</c>；不存在返回 <c>"0"</c>。
        /// <para>mtime 用 UTC ticks（不格式化），避免时区 / 精度差异导致"看起来没变其实变了"。</para>
        /// </summary>
        public static string StampOf(string flowId, string nodeId)
        {
            if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(nodeId)) return "0";

            string png = PngPathOf(flowId, nodeId);
            string json = MetaPathOf(flowId, nodeId);
            if (!File.Exists(png) || !File.Exists(json)) return "0";

            return FileStamp(json) + "|" + FileStamp(png);
        }

        private static string FileStamp(string path)
        {
            var fi = new FileInfo(path);
            return fi.Length.ToString(CultureInfo.InvariantCulture) + "@"
                 + fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 把流程 Id / 节点 Id 洗成安全目录名。
        /// <para>Id 是本系统生成的 GUID，本来安全；但 <c>templates</c> 目录允许人工整理/拷贝，
        /// 一旦有人手改了目录名带出非法字符，<see cref="Path.Combine"/> 之后的访问会抛异常，
        /// 这里统一兜住，绝不让"手工整理"变成"程序崩溃"。</para>
        /// </summary>
        private static string SanitizeId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "_";

            char[] bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(id.Length);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool invalid = false;
                for (int k = 0; k < bad.Length; k++)
                    if (bad[k] == c) { invalid = true; break; }

                // 目录分隔符即便在别的平台上合法，也必须挡掉：否则 "..\x" 能跳出 Templates 根
                if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar) invalid = true;
                sb.Append(invalid ? '_' : c);
            }

            string s = sb.ToString();
            if (s == "..") s = "__";
            return s.Length == 0 ? "_" : s;
        }

        // =====================================================================
        //  读取
        // =====================================================================

        /// <summary>读取模板资产；失败时 <paramref name="error"/> 给出可区分的三类原因之一。</summary>
        public static TemplateAsset Load(string flowId, string nodeId,
                                         out TemplateLoadError error, out string message)
        {
            error = TemplateLoadError.None;
            message = null;

            if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(nodeId))
            {
                error = TemplateLoadError.FileMissing;
                message = "流程或节点标识为空，无法定位模板。";
                return null;
            }

            string pngPath = PngPathOf(flowId, nodeId);
            string metaPath = MetaPathOf(flowId, nodeId);

            if (!File.Exists(metaPath) || !File.Exists(pngPath))
            {
                error = TemplateLoadError.FileMissing;
                message = "模板文件不存在（" + (File.Exists(metaPath) ? ImageFileName : MetaFileName) + " 缺失）。";
                return null;
            }

            TemplateMeta meta;
            try
            {
                meta = Json.LoadFromFile<TemplateMeta>(metaPath);
            }
            catch (Exception ex)
            {
                error = TemplateLoadError.CorruptedOrVersionMismatch;
                message = "模板元数据无法解析（" + ex.GetType().Name + "：" + ex.Message + "）";
                return null;
            }

            if (meta == null)
            {
                error = TemplateLoadError.CorruptedOrVersionMismatch;
                message = "模板元数据为空或格式不正确。";
                return null;
            }

            if (meta.SchemaVersion != SchemaVersion)
            {
                error = TemplateLoadError.CorruptedOrVersionMismatch;
                message = string.Format(CultureInfo.InvariantCulture,
                    "模板文件版本不兼容（文件 {0}，本程序支持 {1}），请重新创建模板。",
                    meta.SchemaVersion, SchemaVersion);
                return null;
            }

            string sha;
            try
            {
                sha = Sha256OfFile(pngPath);
            }
            catch (Exception ex)
            {
                error = TemplateLoadError.CorruptedOrVersionMismatch;
                message = "模板图读取失败（" + ex.Message + "）。";
                return null;
            }

            if (!string.IsNullOrEmpty(meta.ImageSha256)
                && !string.Equals(sha, meta.ImageSha256, StringComparison.OrdinalIgnoreCase))
            {
                error = TemplateLoadError.CorruptedOrVersionMismatch;
                message = "模板图与元数据不一致（文件被外部修改或写入不完整），请重新创建模板。";
                return null;
            }

            Bitmap bmp;
            try
            {
                bmp = LoadBitmapNoLock(pngPath);
            }
            catch (Exception ex)
            {
                error = TemplateLoadError.CorruptedOrVersionMismatch;
                message = "模板图已损坏或不支持的格式（" + ex.Message + "）。";
                return null;
            }

            if (bmp == null)
            {
                error = TemplateLoadError.CorruptedOrVersionMismatch;
                message = "模板图为空。";
                return null;
            }

            // 灰度模板的强制归一化：
            // GDI+ 的 PNG 编解码不做"像素格式保真"—— 8bpp 身份灰度图存成 PNG 再解码回来，
            // 拿到的往往是 Format32bppArgb（GDI+ 把灰度 PNG 当 32bpp 解）。
            // 这本身不丢信息（R=G=B=原灰度值），但会让算法库的 GrayInput 走"24bpp 转灰度"路径，
            // 白白多一次逐像素乘法；更糟的是此后 IsIdentityGray 判 false，任何人拿它做
            // "灰度直通"判断都会判错。所以这里显式转回身份灰度 8bpp，把不变量钉在存储层。
            if (string.Equals(meta.ImagePixelFormat, "8bppIndexed", StringComparison.OrdinalIgnoreCase)
                && !IsIdentityGray(bmp))
            {
                try
                {
                    Bitmap gray = ConvertToIdentityGray(bmp);
                    bmp.Dispose();
                    bmp = gray;
                }
                catch (Exception ex)
                {
                    bmp.Dispose();
                    error = TemplateLoadError.CorruptedOrVersionMismatch;
                    message = "模板图灰度归一化失败（" + ex.Message + "）。";
                    return null;
                }
            }

            var asset = new TemplateAsset
            {
                Meta = meta,
                Template = bmp,
                Dir = DirOf(flowId, nodeId),
                Stamp = StampOf(flowId, nodeId)
            };
            return asset;
        }

        /// <summary>
        /// 读取模板资产，并额外判定"是否与当前节点设置匹配"。
        /// <para>
        /// 判两件事：① 算法类型一致（形状匹配的模板不能给 NCC 用，反之亦然）；
        /// ② 模板记录的 ROI 与节点当前的 <c>TemplateX/Y/W/H</c> 一致 —— 不一致说明用户改过 ROI，
        /// 而模板图正是那个 ROI 的像素，必须重新制作（<see cref="TemplateLoadError.MismatchWithNode"/>）。
        /// </para>
        /// </summary>
        /// <param name="expectedRoi">节点当前的模板 ROI（宽高为 0 表示整帧）。</param>
        /// <param name="expectedAlgorithm">期望算法：<c>"ShapeMatch"</c> / <c>"TemplateMatch"</c>。</param>
        public static TemplateAsset Load(string flowId, string nodeId,
                                         string expectedAlgorithm, Rectangle expectedRoi,
                                         out TemplateLoadError error, out string message)
        {
            TemplateAsset asset = Load(flowId, nodeId, out error, out message);
            if (asset == null) return null;

            if (!string.IsNullOrEmpty(expectedAlgorithm)
                && !string.Equals(asset.Meta.Algorithm, expectedAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                string detail = string.Format(CultureInfo.InvariantCulture,
                    "模板是用「{0}」创建的，当前节点是「{1}」，请重新创建模板。",
                    DisplayAlgorithm(asset.Meta.Algorithm), DisplayAlgorithm(expectedAlgorithm));
                asset.Template.Dispose();
                error = TemplateLoadError.MismatchWithNode;
                message = detail;
                return null;
            }

            if (!RoiEquals(asset.Meta, expectedRoi))
            {
                asset.Template.Dispose();
                error = TemplateLoadError.MismatchWithNode;
                message = "模板与当前 ROI 不匹配，请重新创建模板。";
                return null;
            }

            error = TemplateLoadError.None;
            message = null;
            return asset;
        }

        /// <summary>把 <see cref="TemplateLoadError"/> 转成给用户看的一句话（不含细节）。</summary>
        public static string DescribeError(TemplateLoadError error)
        {
            switch (error)
            {
                case TemplateLoadError.FileMissing: return "未配置模板";
                case TemplateLoadError.CorruptedOrVersionMismatch: return "模板文件损坏或版本不兼容";
                case TemplateLoadError.MismatchWithNode: return "模板与当前设置不匹配";
                default: return "已就绪";
            }
        }

        /// <summary>算法键 → 中文名。</summary>
        public static string DisplayAlgorithm(string algorithm)
        {
            if (string.Equals(algorithm, "TemplateMatch", StringComparison.OrdinalIgnoreCase)) return "模板匹配(NCC)";
            if (string.Equals(algorithm, "ShapeMatch", StringComparison.OrdinalIgnoreCase)) return "形状匹配";
            return string.IsNullOrEmpty(algorithm) ? "未知" : algorithm;
        }

        private static bool RoiEquals(TemplateMeta meta, Rectangle expected)
        {
            if (meta == null) return false;

            // "宽或高为 0 = 整帧"是既有约定：两边都按 0 处理即视为相同
            int ew = expected.Width > 0 && expected.Height > 0 ? expected.Width : 0;
            int eh = expected.Width > 0 && expected.Height > 0 ? expected.Height : 0;
            int ex = ew == 0 ? 0 : expected.X;
            int ey = eh == 0 ? 0 : expected.Y;

            int mw = meta.TemplateW > 0 && meta.TemplateH > 0 ? meta.TemplateW : 0;
            int mh = meta.TemplateW > 0 && meta.TemplateH > 0 ? meta.TemplateH : 0;
            int mx = mw == 0 ? 0 : meta.TemplateX;
            int my = mh == 0 ? 0 : meta.TemplateY;

            return ex == mx && ey == my && ew == mw && eh == mh;
        }

        /// <summary>
        /// 从文件读图并<strong>立刻复制一份</strong>：<c>using</c> 结束后源流与探针图一起释放，
        /// 不会长期占用文件句柄 —— 否则「删除模板」「重做模板」「外部拷贝覆盖」三条路径全部失败
        /// （Windows 上被 GDI+ 打开的文件是独占读的）。同款先例见 <c>FlowPreviewPanel.LoadImageFromFile</c>。
        /// </summary>
        public static Bitmap LoadBitmapNoLock(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var probe = new Bitmap(fs))
            {
                // 复制成与本程序灰度约定一致的位图：8bpp 一律转成"身份灰度调色板"，
                // 其余保持 24bpp RGB。这样运行期 GrayInput 才会走直通路径，灰度值不偏移。
                if (probe.PixelFormat == PixelFormat.Format8bppIndexed && !IsIdentityGray(probe))
                    return ConvertToIdentityGray(probe);

                if (probe.PixelFormat == PixelFormat.Format8bppIndexed
                    || probe.PixelFormat == PixelFormat.Format24bppRgb)
                {
                    return new Bitmap(probe);
                }

                var copy = new Bitmap(probe.Width, probe.Height, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(copy))
                    g.DrawImage(probe, 0, 0, probe.Width, probe.Height);
                return copy;
            }
        }

        // =====================================================================
        //  保存
        // =====================================================================

        /// <summary>
        /// 保存模板资产（PNG + JSON），并<strong>回读校验</strong>（重新从磁盘读图 → 比对 SHA-256 / 尺寸 / 像素格式）。
        /// <para>
        /// 【为什么必须回读】"写成功"只说明 API 没抛异常，说明不了"下次能读回来"。
        /// 磁盘满、杀软锁文件、编码器静默降质都在写完之后才暴露；回读一次把失败点前移到保存时刻，
        /// 用户当场就能看到"保存失败"而不是第二天开机发现模板坏了。
        /// </para>
        /// <para>
        /// 【回读只做 I/O 校验】PNG 解码 → SHA-256 一致 → 尺寸/格式一致。本方法刻意<strong>不训练</strong>：
        /// 训练需要独占的 <see cref="VisionAlgo"/>，把它塞进存储层会让"存文件"顺手持有原生上下文，
        /// 也违反"存储层不依赖算法层"的分层。<strong>用读回的图重训一次</strong>由调用方
        /// （<c>TemplateForm</c>）在保存成功后立刻执行，效果等价、职责更清楚。
        /// </para>
        /// </summary>
        /// <param name="templateImage">已裁剪好的模板图（不是整幅原图）。</param>
        /// <param name="meta">元数据；本方法会补齐尺寸 / SHA-256 / 版本 / 文件名等字段。</param>
        public static bool Save(string flowId, string nodeId, Bitmap templateImage,
                                TemplateMeta meta, out string message)
        {
            message = null;

            if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(nodeId))
            {
                message = "流程或节点标识为空，无法保存模板。";
                return false;
            }
            if (templateImage == null)
            {
                message = "模板图为空，无法保存。";
                return false;
            }
            if (meta == null) meta = TemplateMeta.CreateDefault();

            string dir = DirOf(flowId, nodeId);
            string pngPath = Path.Combine(dir, ImageFileName);
            string metaPath = Path.Combine(dir, MetaFileName);

            Bitmap prepared = null;      // 需要改格式时由本方法创建，负责释放
            Bitmap grayTemp = null;
            try
            {
                Directory.CreateDirectory(dir);

                Bitmap toWrite = templateImage;

                // ① 8bpp 但调色板不是身份的：必须换成身份灰度，否则下次加载灰度值整体偏移 → 训练结果变化
                if (templateImage.PixelFormat == PixelFormat.Format8bppIndexed && !IsIdentityGray(templateImage))
                {
                    prepared = ConvertToIdentityGray(templateImage);
                    toWrite = prepared;
                }

                WritePngAtomically(toWrite, pngPath);
                long size = new FileInfo(pngPath).Length;

                // ② 体积控制：彩色且超限时先降成灰度（不降采样！降质会破坏"分数差 ≤0.01"）
                if (size > MaxPngBytes && toWrite.PixelFormat != PixelFormat.Format8bppIndexed)
                {
                    grayTemp = ConvertToIdentityGray(toWrite);
                    WritePngAtomically(grayTemp, pngPath);
                    size = new FileInfo(pngPath).Length;

                    if (size > MaxPngBytes)
                    {
                        TryDelete(pngPath);
                        message = string.Format(CultureInfo.InvariantCulture,
                            "模板区域过大（{0}×{1} 像素，压缩后 {2:F1} MB，上限 {3:F1} MB），建议缩小到 2000×2000 以内。",
                            templateImage.Width, templateImage.Height,
                            size / 1048576.0, MaxPngBytes / 1048576.0);
                        return false;
                    }

                    // 转成灰度后以磁盘上的实际内容为准
                    toWrite = grayTemp;
                }

                // ③ 元数据补齐（以真正落盘的那份为准）
                meta.SchemaVersion = SchemaVersion;
                meta.ImageFile = ImageFileName;
                meta.ImageW = toWrite.Width;
                meta.ImageH = toWrite.Height;
                meta.ImagePixelFormat = PixelFormatName(toWrite.PixelFormat);
                meta.ImageSha256 = Sha256OfFile(pngPath);
                if (string.IsNullOrEmpty(meta.CreatedUtc))
                    meta.CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(meta.AppVersion))
                    meta.AppVersion = SafeAppVersion();

                Json.SaveToFile(metaPath, meta);

                // ④ 回读校验：能从磁盘读回来、字节一致、尺寸与格式一致
                TemplateLoadError err;
                string loadMsg;
                TemplateAsset probe = Load(flowId, nodeId, out err, out loadMsg);
                if (probe == null)
                {
                    TryDelete(pngPath);
                    TryDelete(metaPath);
                    message = "模板保存后回读失败：" + (loadMsg ?? DescribeError(err));
                    return false;
                }

                try
                {
                    if (probe.Template.Width != meta.ImageW || probe.Template.Height != meta.ImageH)
                    {
                        TryDelete(pngPath);
                        TryDelete(metaPath);
                        message = string.Format(CultureInfo.InvariantCulture,
                            "模板保存后回读尺寸不一致（应为 {0}×{1}，实际 {2}×{3}）。",
                            meta.ImageW, meta.ImageH, probe.Template.Width, probe.Template.Height);
                        return false;
                    }
                }
                finally
                {
                    probe.Template.Dispose();
                }

                message = string.Format(CultureInfo.InvariantCulture,
                    "已保存模板：{0}×{1}，{2:F1} KB，{3}。",
                    meta.ImageW, meta.ImageH, size / 1024.0, meta.ImagePixelFormat);
                return true;
            }
            catch (Exception ex)
            {
                message = "保存模板失败：" + ex.Message;
                return false;
            }
            finally
            {
                if (grayTemp != null) grayTemp.Dispose();
                if (prepared != null) prepared.Dispose();
            }
        }

        /// <summary>删除模板资产（两个文件 + 空目录）；不存在也算成功。</summary>
        public static bool Delete(string flowId, string nodeId, out string message)
        {
            message = null;
            if (string.IsNullOrEmpty(flowId) || string.IsNullOrEmpty(nodeId))
            {
                message = "流程或节点标识为空，无法删除模板。";
                return false;
            }

            string pngPath = PngPathOf(flowId, nodeId);
            string metaPath = MetaPathOf(flowId, nodeId);

            try
            {
                bool existed = File.Exists(pngPath) || File.Exists(metaPath);
                TryDelete(pngPath);
                TryDelete(metaPath);

                string dir = DirOf(flowId, nodeId);
                if (Directory.Exists(dir))
                {
                    // 只删空目录：目录里若还有别的东西（用户放进去的备注 / 备份），不要越权清理
                    try
                    {
                        if (Directory.GetFileSystemEntries(dir).Length == 0)
                            Directory.Delete(dir);
                    }
                    catch (Exception)
                    {
                        // 删不掉目录不影响"模板已失效"这个事实
                    }
                }

                message = existed ? "模板已删除。" : "该节点本来就没有模板。";
                return true;
            }
            catch (Exception ex)
            {
                message = "删除模板失败：" + ex.Message;
                return false;
            }
        }

        // =====================================================================
        //  位图工具
        // =====================================================================

        /// <summary>8bpp 索引图的调色板是否为「灰度且索引值等于亮度」（与算法库同一判据）。</summary>
        public static bool IsIdentityGray(Bitmap bmp)
        {
            if (bmp == null || bmp.PixelFormat != PixelFormat.Format8bppIndexed) return false;

            ColorPalette pal = bmp.Palette;
            if (pal.Entries.Length < 256) return false;

            for (int i = 0; i < 256; i++)
            {
                Color c = pal.Entries[i];
                if (c.R != i || c.G != i || c.B != i) return false;
            }
            return true;
        }

        /// <summary>
        /// 转成"身份灰度"8bpp 位图（调用方负责 Dispose）。
        /// <para>
        /// 直接 <c>new Bitmap(w, h, Format8bppIndexed)</c> 再 <c>Graphics.FromImage</c> 是<strong>不行</strong>的：
        /// GDI+ 无法在索引位图上创建 <c>Graphics</c>。所以这里手工 <c>LockBits</c> 逐像素取 Rec.601 亮度，
        /// 调色板交给 <see cref="VisionAlgo.CreateGrayBitmap"/>（它建出来的就是身份灰度调色板）。
        /// </para>
        /// </summary>
        public static Bitmap ConvertToIdentityGray(Bitmap src)
        {
            if (src == null) return null;

            int w = src.Width;
            int h = src.Height;

            // ① 先归一化为 24bpp（索引图 / 32bpp / 灰度图都走这一条，避免每种格式单独写一遍）
            Bitmap rgb = src;
            Bitmap rgbOwned = null;
            if (src.PixelFormat != PixelFormat.Format24bppRgb)
            {
                rgbOwned = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(rgbOwned))
                {
                    // 不设 PixelOffsetMode：与 VisionAlgo.GrayInput 的转换保持逐像素一致，
                    // 两边只要有一处不同，同一张图算出的灰度就会有 ±1 的差 → 训练结果变化。
                    g.DrawImage(src, 0, 0, w, h);
                }
                rgb = rgbOwned;
            }

            Bitmap dst = null;
            BitmapData dstData = null;
            try
            {
                dst = VisionAlgo.CreateGrayBitmap(w, h, out dstData);

                BitmapData srcData = rgb.LockBits(new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    byte[] row = new byte[srcData.Stride];
                    byte[] outRow = new byte[dstData.Stride];

                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(IntPtr.Add(srcData.Scan0, y * srcData.Stride), row, 0, srcData.Stride);
                        for (int x = 0; x < w; x++)
                        {
                            int i = x * 3;
                            // Rec.601 亮度，与 VisionAlgo.GrayInput 完全同一公式：
                            // 两条路径算出的灰度不一致 → 训练结果不一致 → "分数差 ≤0.01" 直接挂
                            outRow[x] = (byte)((row[i] * 114 + row[i + 1] * 587 + row[i + 2] * 299) / 1000);
                        }
                        Marshal.Copy(outRow, 0, IntPtr.Add(dstData.Scan0, y * dstData.Stride), outRow.Length);
                    }
                }
                finally
                {
                    rgb.UnlockBits(srcData);
                }

                dst.UnlockBits(dstData);
                dstData = null;
                return dst;
            }
            catch (Exception)
            {
                if (dst != null) dst.Dispose();
                throw;
            }
            finally
            {
                if (dstData != null && dst != null) dst.UnlockBits(dstData);
                if (rgbOwned != null) rgbOwned.Dispose();
            }
        }

        // =====================================================================
        //  内部工具
        // =====================================================================

        /// <summary>
        /// 原子式写 PNG：先写 <c>.tmp</c> 再换名。避免"写到一半断电／被杀软打断"留下半张图，
        /// 而那半张图恰好能让 <c>new Bitmap()</c> 成功解码出上半部分 —— 这种损坏最难查。
        /// </summary>
        private static void WritePngAtomically(Bitmap image, string path)
        {
            string tmp = path + ".tmp";
            TryDelete(tmp);
            image.Save(tmp, ImageFormat.Png);

            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // 删除失败交给上层用"回读失败 / 加载失败"暴露，这里不抛
            }
        }

        /// <summary>算文件的 SHA-256（十六进制小写）。</summary>
        public static string Sha256OfFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static string PixelFormatName(PixelFormat format)
        {
            return format == PixelFormat.Format8bppIndexed ? "8bppIndexed" : "24bppRgb";
        }

        private static string SafeAppVersion()
        {
            try
            {
                return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
            catch (Exception)
            {
                return "0.0.0.0";
            }
        }

        /// <summary>
        /// 复制整条流程的模板资产（复制流程时用）。
        /// <para>
        /// 【为什么必须复制】模板按 <c>{flowId}\{nodeId}</c> 落盘：只复制 FlowDefinition 而不复制目录，
        /// 新流程里的匹配/OCR 节点会全都报"模板资产不存在"。
        /// </para>
        /// </summary>
        /// <param name="srcFlowId">源流程 Id。</param>
        /// <param name="dstFlowId">目标流程 Id。</param>
        /// <param name="idMap">源节点 Id → 目标节点 Id。</param>
        /// <returns>复制成功的节点数。</returns>
        public static int CopyFlowAssets(string srcFlowId, string dstFlowId,
                                         IDictionary<string, string> idMap)
        {
            int copied = 0;
            if (string.IsNullOrEmpty(srcFlowId) || string.IsNullOrEmpty(dstFlowId)) return 0;
            if (idMap == null || idMap.Count == 0) return 0;

            foreach (KeyValuePair<string, string> kv in idMap)
            {
                string srcDir = DirOf(srcFlowId, kv.Key);
                string dstDir = DirOf(dstFlowId, kv.Value);

                try
                {
                    if (!Directory.Exists(srcDir)) continue;
                    if (Directory.Exists(dstDir)) continue;   // 目标已存在：不覆盖（宁留不丢）

                    Directory.CreateDirectory(dstDir);
                    foreach (string file in Directory.GetFiles(srcDir))
                    {
                        string name = Path.GetFileName(file);
                        File.Copy(file, Path.Combine(dstDir, name), false);
                    }
                    copied++;
                }
                catch (Exception)
                {
                    // 单个节点复制失败不影响整条流程复制；调用方按返回值提示
                }
            }
            return copied;
        }

    }
}
