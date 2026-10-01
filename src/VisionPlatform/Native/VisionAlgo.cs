using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VisionPlatform.Core;

namespace VisionPlatform.Native
{
    #region 与 C 端对应的结构体

    [StructLayout(LayoutKind.Sequential)]
    public struct VA_Image
    {
        public IntPtr data;
        public int width;
        public int height;
        public int channels;
        public int stride;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VA_PreprocessParam
    {
        public int to_gray;
        public int blur_ksize;
        public int threshold;
        public int invert;
        public int morph_op;
        public int morph_ksize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VA_Blob
    {
        public double cx;
        public double cy;
        public double area;
        public double width;
        public double height;
        public double angle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VA_Circle
    {
        public double cx;
        public double cy;
        public double radius;
        public double residual;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VA_MatchResult
    {
        public double x;
        public double y;
        public double width;
        public double height;
        public double score;
        public double angle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VA_Line
    {
        public double x1;
        public double y1;
        public double x2;
        public double y2;
    }

    /// <summary>形状匹配参数（对应 C 端 VA_ShapeParam）。全为标量，可直接 Sequential 映射。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_ShapeParam
    {
        public double angle_start;
        public double angle_end;
        public double angle_step;
        public double scale_min;
        public double scale_max;
        public double scale_step;
        public int    pyramid_levels;
        public double min_score;
        public int    max_matches;
        public double max_overlap;
        public int    subpixel;
        public double greediness;   // 贪婪系数 0~1，越大越快；0 = 关闭启发式
    }

    /// <summary>训练参数（对应 C 端 VA_ShapeTrainParam）：控制特征点数量与空间稀疏间距。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_ShapeTrainParam
    {
        public double contrast;
        public int max_features;
        public double min_feature_dist;
    }

    /// <summary>模型特征点（对应 C 端 VA_ShapePoint）：位置相对模板中心，方向为单位梯度方向。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_ShapePoint
    {
        public double x;
        public double y;
        public double dx;
        public double dy;
    }

    /// <summary>单个形状匹配结果（对应 C 端 VA_ShapeMatch）。(cx, cy) 为目标中心。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_ShapeMatch
    {
        public double cx;
        public double cy;
        public double angle;
        public double scale;
        public double score;
        public double width;
        public double height;
    }

    #endregion

    /// <summary>算法库返回码。</summary>
    public static class VaResult
    {
        public const int OK = 0;
        public const int ErrHandle = 1;
        public const int ErrArg = 2;
        public const int ErrSize = 3;
        public const int ErrType = 4;
        public const int ErrInternal = 5;
        public const int ErrNoTemplate = 6;

        public static string ToString(int code)
        {
            switch (code)
            {
                case OK: return "成功";
                case ErrHandle: return "句柄无效";
                case ErrArg: return "参数非法";
                case ErrSize: return "尺寸不匹配";
                case ErrType: return "图像类型不支持";
                case ErrInternal: return "算法内部错误";
                case ErrNoTemplate: return "未训练模板";
                default: return "未知错误 " + code;
            }
        }
    }

    internal static class NativeMethods
    {
        private const string Dll = "VisionAlgo.Native.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_get_version();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr va_create();

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern void va_destroy(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_get_last_error(IntPtr handle, byte[] buffer, int size);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_preprocess(IntPtr handle, ref VA_Image src,
                                               ref VA_Image dst, ref VA_PreprocessParam param);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_blob_analyze(IntPtr handle, ref VA_Image gray,
                                                 double min_area, double max_area,
                                                 [In, Out] VA_Blob[] out_blobs,
                                                 int max_count, out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_sharpness(IntPtr handle, ref VA_Image gray, out double score);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_fit_circle(IntPtr handle, ref VA_Image gray, out VA_Circle circle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_fit_line(IntPtr handle, ref VA_Image gray, out VA_Line line);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_train_template(IntPtr handle, ref VA_Image gray, int[] roi);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_match_template(IntPtr handle, ref VA_Image gray,
                                                   out VA_MatchResult result);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_edge_count(IntPtr handle, ref VA_Image gray,
                                               int scan_row, int threshold_delta, out int count);

        // ---- 形状匹配（基于边缘轮廓）----

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_create_shape_model(IntPtr handle, ref VA_Image gray,
                                                       int[] roi, double contrast);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_get_shape_model_info(IntPtr handle, out int point_count,
                                                         out double width, out double height);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_clear_shape_model(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_find_shape_model(IntPtr handle, ref VA_Image gray,
                                                     ref VA_ShapeParam param, int[] search_roi,
                                                     [In, Out] VA_ShapeMatch[] out_matches,
                                                     int max_count, out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_create_shape_model_ex(IntPtr handle, ref VA_Image gray,
                                                           int[] roi, ref VA_ShapeTrainParam param);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_get_shape_model_points(IntPtr handle,
                                                            [In, Out] VA_ShapePoint[] out_points,
                                                            int max_count, out int out_count);
    }

    /// <summary>
    /// C++ 算法库（VisionAlgo.Native.dll）的托管封装。
    /// 每个实例对应库内的一个上下文句柄，模板训练结果保存在该上下文中。
    /// </summary>
    public class VisionAlgo : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private bool _disposed;

        public VisionAlgo()
        {
            _handle = NativeMethods.va_create();
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("无法创建算法上下文，请确认 VisionAlgo.Native.dll 与 OpenCV 运行时已就位。");
        }

        public bool IsValid { get { return _handle != IntPtr.Zero; } }

        public static int LibraryVersion
        {
            get
            {
                try { return NativeMethods.va_get_version(); }
                catch (Exception) { return -1; }
            }
        }

        /// <summary>取算法库内部记录的最近一次错误文本。</summary>
        public string LastError
        {
            get
            {
                if (_handle == IntPtr.Zero) return string.Empty;
                byte[] buffer = new byte[1024];
                int n = NativeMethods.va_get_last_error(_handle, buffer, buffer.Length);
                if (n <= 0) return string.Empty;
                return System.Text.Encoding.UTF8.GetString(buffer, 0, n);
            }
        }

        #region 算法方法

        /// <summary>预处理：灰度化 → 高斯 → 二值化 → 形态学。返回单通道灰度位图。</summary>
        public OpResult<Bitmap> Preprocess(Bitmap src, PreprocessParam param)
        {
            if (src == null) return OpResult<Bitmap>.Fail("输入图像为空");

            // 彩色图 / 不支持的像素格式先归一化
            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(src))
            {
                Bitmap dst = null;
                BitmapData dstData = null;

                try
                {
                    VA_Image srcImg = gi.Image;

                    dst = CreateGrayBitmap(src.Width, src.Height, out dstData);

                VA_Image dstImg = new VA_Image();
                dstImg.data = dstData.Scan0;
                dstImg.width = dst.Width;
                dstImg.height = dst.Height;
                dstImg.channels = 1;
                dstImg.stride = dstData.Stride;

                VA_PreprocessParam p = param.ToNative();

                int rc = NativeMethods.va_preprocess(_handle, ref srcImg, ref dstImg, ref p);
                if (rc != VaResult.OK)
                    return OpResult<Bitmap>.Fail(VaResult.ToString(rc) + "：" + LastError);

                return OpResult<Bitmap>.Ok(dst);
            }
            catch (Exception ex)
            {
                if (dst != null) { dst.Dispose(); dst = null; }
                return OpResult<Bitmap>.Fail("预处理失败：" + ex.Message);
            }
                finally
                {
                    if (dstData != null && dst != null) dst.UnlockBits(dstData);
                }
            }
        }

        /// <summary>连通域分析。输入可以是灰度图或二值图。</summary>
        public OpResult<List<BlobResult>> AnalyzeBlobs(Bitmap gray, double minArea, double maxArea = 0, int maxCount = 256)
        {
            if (gray == null) return OpResult<List<BlobResult>>.Fail("输入图像为空");

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                VA_Image img = gi.Image;

                VA_Blob[] raw = new VA_Blob[maxCount];
                int count = 0;

                int rc = NativeMethods.va_blob_analyze(_handle, ref img, minArea, maxArea, raw, maxCount, out count);
                if (rc != VaResult.OK)
                    return OpResult<List<BlobResult>>.Fail(VaResult.ToString(rc) + "：" + LastError);

                var list = new List<BlobResult>();
                for (int i = 0; i < count && i < raw.Length; i++)
                    list.Add(BlobResult.FromNative(raw[i]));

                return OpResult<List<BlobResult>>.Ok(list);
            }
            catch (Exception ex)
            {
                return OpResult<List<BlobResult>>.Fail("Blob 分析失败：" + ex.Message);
            }
            }
        }

        /// <summary>清晰度评价：拉普拉斯方差，越大越清晰。</summary>
        public OpResult<double> Sharpness(Bitmap gray)
        {
            if (gray == null) return OpResult<double>.Fail("输入图像为空");

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                VA_Image img = gi.Image;

                double score;
                int rc = NativeMethods.va_sharpness(_handle, ref img, out score);
                if (rc != VaResult.OK)
                    return OpResult<double>.Fail(VaResult.ToString(rc) + "：" + LastError);

                return OpResult<double>.Ok(score);
            }
            catch (Exception ex)
            {
                return OpResult<double>.Fail("清晰度评价失败：" + ex.Message);
            }
            }
        }

        /// <summary>对最大轮廓做圆拟合。</summary>
        public OpResult<CircleResult> FitCircle(Bitmap gray)
        {
            if (gray == null) return OpResult<CircleResult>.Fail("输入图像为空");

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                VA_Image img = gi.Image;

                VA_Circle c;
                int rc = NativeMethods.va_fit_circle(_handle, ref img, out c);
                if (rc != VaResult.OK)
                    return OpResult<CircleResult>.Fail(VaResult.ToString(rc) + "：" + LastError);

                return OpResult<CircleResult>.Ok(new CircleResult(c.cx, c.cy, c.radius, c.residual));
            }
            catch (Exception ex)
            {
                return OpResult<CircleResult>.Fail("圆拟合失败：" + ex.Message);
            }
            }
        }

        /// <summary>对最大轮廓做直线拟合。</summary>
        public OpResult<LineResult> FitLine(Bitmap gray)
        {
            if (gray == null) return OpResult<LineResult>.Fail("输入图像为空");

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                VA_Image img = gi.Image;

                VA_Line l;
                int rc = NativeMethods.va_fit_line(_handle, ref img, out l);
                if (rc != VaResult.OK)
                    return OpResult<LineResult>.Fail(VaResult.ToString(rc) + "：" + LastError);

                return OpResult<LineResult>.Ok(new LineResult(l.x1, l.y1, l.x2, l.y2));
            }
            catch (Exception ex)
            {
                return OpResult<LineResult>.Fail("直线拟合失败：" + ex.Message);
            }
            }
        }

        /// <summary>训练模板。roi 为空表示整图。</summary>
        public OpResult TrainTemplate(Bitmap gray, Rectangle roi)
        {
            if (gray == null) return OpResult.Fail("输入图像为空");

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                VA_Image img = gi.Image;

                int[] roiArr = null;
                if (roi.Width > 0 && roi.Height > 0)
                    roiArr = new[] { roi.X, roi.Y, roi.Width, roi.Height };

                int rc = NativeMethods.va_train_template(_handle, ref img, roiArr);
                if (rc != VaResult.OK)
                    return OpResult.Fail(VaResult.ToString(rc) + "：" + LastError);

                return OpResult.Ok("模板已训练");
            }
            catch (Exception ex)
            {
                return OpResult.Fail("模板训练失败：" + ex.Message);
            }
            }
        }

        /// <summary>模板匹配。</summary>
        public OpResult<MatchResult> MatchTemplate(Bitmap gray)
        {
            if (gray == null) return OpResult<MatchResult>.Fail("输入图像为空");

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                VA_Image img = gi.Image;

                VA_MatchResult r;
                int rc = NativeMethods.va_match_template(_handle, ref img, out r);
                if (rc != VaResult.OK)
                    return OpResult<MatchResult>.Fail(VaResult.ToString(rc) + "：" + LastError);

                return OpResult<MatchResult>.Ok(new MatchResult(r.x, r.y, r.width, r.height, r.score));
            }
            catch (Exception ex)
            {
                return OpResult<MatchResult>.Fail("模板匹配失败：" + ex.Message);
            }
            }
        }

        // ---------------------- 形状匹配（基于边缘轮廓）----------------------

        /// <summary>用 ROI 区域训练形状匹配模型（可指定特征点数与稀疏间距）。roi 宽高为 0 时表示整图。</summary>
        public OpResult<ShapeModelInfo> CreateShapeModel(Bitmap gray, Rectangle roi, ShapeTrainParam train)
        {
            if (gray == null) return OpResult<ShapeModelInfo>.Fail("输入图像为空");
            if (train == null) train = ShapeTrainParam.Default();

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                    VA_Image img = gi.Image;

                    int[] roiArr = null;
                    if (roi.Width > 0 && roi.Height > 0)
                        roiArr = new[] { roi.X, roi.Y, roi.Width, roi.Height };

                    VA_ShapeTrainParam tp = train.ToNative();

                    int rc = NativeMethods.va_create_shape_model_ex(_handle, ref img, roiArr, ref tp);
                    if (rc != VaResult.OK)
                        return OpResult<ShapeModelInfo>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    int pts; double w, h;
                    NativeMethods.va_get_shape_model_info(_handle, out pts, out w, out h);
                    return OpResult<ShapeModelInfo>.Ok(new ShapeModelInfo(pts, w, h));
                }
                catch (Exception ex)
                {
                    return OpResult<ShapeModelInfo>.Fail("形状模型训练失败：" + ex.Message);
                }
            }
        }

        /// <summary>用 ROI 区域训练形状匹配模型（只指定对比度，其余用默认）。roi 宽高为 0 时表示整图。</summary>
        public OpResult<ShapeModelInfo> CreateShapeModel(Bitmap gray, Rectangle roi, double contrast = 30)
        {
            var train = ShapeTrainParam.Default();
            train.Contrast = contrast;
            return CreateShapeModel(gray, roi, train);
        }

        /// <summary>查询当前形状模型信息；尚未训练时返回失败。</summary>
        public OpResult<ShapeModelInfo> GetShapeModelInfo()
        {
            try
            {
                int pts; double w, h;
                int rc = NativeMethods.va_get_shape_model_info(_handle, out pts, out w, out h);
                if (rc != VaResult.OK)
                    return OpResult<ShapeModelInfo>.Fail(VaResult.ToString(rc) + "：" + LastError);

                return OpResult<ShapeModelInfo>.Ok(new ShapeModelInfo(pts, w, h));
            }
            catch (Exception ex)
            {
                return OpResult<ShapeModelInfo>.Fail("查询形状模型失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 导出模型的特征点（边缘点 + 单位梯度方向），用于界面显示模板特征结构。
        /// 坐标相对模板中心，按梯度幅值从大到小排列。
        /// </summary>
        public OpResult<List<ShapeModelPoint>> GetShapeModelPoints(int maxCount = 8000)
        {
            if (maxCount <= 0) maxCount = 8000;

            try
            {
                VA_ShapePoint[] raw = new VA_ShapePoint[maxCount];
                int count = 0;

                int rc = NativeMethods.va_get_shape_model_points(_handle, raw, maxCount, out count);
                if (rc != VaResult.OK)
                    return OpResult<List<ShapeModelPoint>>.Fail(VaResult.ToString(rc) + "：" + LastError);

                var list = new List<ShapeModelPoint>(count);
                for (int i = 0; i < count && i < raw.Length; i++)
                    list.Add(ShapeModelPoint.FromNative(raw[i]));

                return OpResult<List<ShapeModelPoint>>.Ok(list);
            }
            catch (Exception ex)
            {
                return OpResult<List<ShapeModelPoint>>.Fail("读取模型特征点失败：" + ex.Message);
            }
        }

        /// <summary>清除形状匹配模型。</summary>
        public OpResult ClearShapeModel()
        {
            int rc = NativeMethods.va_clear_shape_model(_handle);
            return rc == VaResult.OK
                ? OpResult.Ok("形状模型已清除")
                : OpResult.Fail(VaResult.ToString(rc) + "：" + LastError);
        }

        /// <summary>
        /// 形状匹配：在 gray 中搜索已训练的形状模型。
        /// searchRoi 限制「模板中心」可出现的位置，宽高为 0 表示整图。
        /// </summary>
        public OpResult<List<ShapeMatchResult>> FindShapeModel(Bitmap gray, ShapeParam param, Rectangle searchRoi)
        {
            if (gray == null) return OpResult<List<ShapeMatchResult>>.Fail("输入图像为空");
            if (param == null) param = ShapeParam.Default();

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                VA_Image img = gi.Image;

                VA_ShapeParam p = param.ToNative();

                int[] roiArr = null;
                if (searchRoi.Width > 0 && searchRoi.Height > 0)
                    roiArr = new[] { searchRoi.X, searchRoi.Y, searchRoi.Width, searchRoi.Height };

                int maxCount = param.MaxMatches > 0 ? param.MaxMatches : 1;
                VA_ShapeMatch[] raw = new VA_ShapeMatch[maxCount];
                int count = 0;

                int rc = NativeMethods.va_find_shape_model(_handle, ref img, ref p, roiArr,
                                                           raw, maxCount, out count);
                if (rc != VaResult.OK)
                    return OpResult<List<ShapeMatchResult>>.Fail(VaResult.ToString(rc) + "：" + LastError);

                var list = new List<ShapeMatchResult>();
                for (int i = 0; i < count && i < raw.Length; i++)
                    list.Add(ShapeMatchResult.FromNative(raw[i]));

                return OpResult<List<ShapeMatchResult>>.Ok(list);
            }
            catch (Exception ex)
            {
                return OpResult<List<ShapeMatchResult>>.Fail("形状匹配失败：" + ex.Message);
            }
            }
        }

        /// <summary>沿指定行统计边缘跳变次数。</summary>
        public OpResult<int> EdgeCount(Bitmap gray, int scanRow, int delta = 30)
        {
            if (gray == null) return OpResult<int>.Fail("输入图像为空");

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                VA_Image img = gi.Image;

                int count;
                int rc = NativeMethods.va_edge_count(_handle, ref img, scanRow, delta, out count);
                if (rc != VaResult.OK)
                    return OpResult<int>.Fail(VaResult.ToString(rc) + "：" + LastError);

                return OpResult<int>.Ok(count);
            }
            catch (Exception ex)
            {
                return OpResult<int>.Fail("边缘计数失败：" + ex.Message);
            }
            }
        }

        #endregion

        #region 辅助

        /// <summary>
        /// 算法库接受的通道数。返回 0 表示像素格式不被直接支持（16bpp / 48bpp / 64bpp 等）。
        /// 实际调用统一走 <see cref="GrayInput"/>，它会先把任意位图转成 8bpp 灰度，
        /// 因此算法库拿到的永远是 channels=1。
        /// </summary>
        internal static int ChannelsOf(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.Format8bppIndexed: return 1;
                case PixelFormat.Format24bppRgb: return 3;
                case PixelFormat.Format32bppRgb:
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb: return 4;
                default: return 0;
            }
        }

        /// <summary>8bpp 索引图的调色板是否为「灰度且索引值等于亮度」。</summary>
        private static bool IsIdentityGrayPalette(Bitmap bmp)
        {
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
        /// 算法输入：统一转成 8bpp 灰度连续缓冲区后再交给算法库。
        /// 彩色图按 Rec.601 亮度公式转灰度；32bpp / 16bpp / 48bpp 以及彩色调色板索引图
        /// 先经 GDI+ 归一化为 24bpp RGB 再取亮度。数据固定在托管数组中（pinned），
        /// 调用期间不会被 GC 移动，Dispose 时释放。
        /// </summary>
        private sealed class GrayInput : IDisposable
        {
            public VA_Image Image;

            private GCHandle _pin;
            private Bitmap _owned;

            public GrayInput(Bitmap src)
            {
                if (src == null) throw new ArgumentNullException("src");

                int w = src.Width;
                int h = src.Height;
                byte[] gray = new byte[w * h];

                bool directIndexed = src.PixelFormat == PixelFormat.Format8bppIndexed
                                     && IsIdentityGrayPalette(src);

                Bitmap work = src;
                if (src.PixelFormat != PixelFormat.Format24bppRgb && !directIndexed)
                {
                    work = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(work))
                        g.DrawImage(src, 0, 0, w, h);
                    _owned = work;
                }

                var rect = new Rectangle(0, 0, w, h);
                BitmapData d = work.LockBits(rect, ImageLockMode.ReadOnly, work.PixelFormat);
                try
                {
                    int stride = d.Stride;
                    byte[] row = new byte[stride];

                    if (work.PixelFormat == PixelFormat.Format8bppIndexed)
                    {
                        for (int y = 0; y < h; y++)
                        {
                            Marshal.Copy(IntPtr.Add(d.Scan0, y * stride), row, 0, stride);
                            int dst = y * w;
                            for (int x = 0; x < w; x++) gray[dst + x] = row[x];
                        }
                    }
                    else
                    {
                        for (int y = 0; y < h; y++)
                        {
                            Marshal.Copy(IntPtr.Add(d.Scan0, y * stride), row, 0, stride);
                            int dst = y * w;
                            for (int x = 0; x < w; x++)
                            {
                                int i = x * 3;
                                // Rec.601 亮度：Y = 0.299R + 0.587G + 0.114B
                                gray[dst + x] = (byte)((row[i] * 114 + row[i + 1] * 587 + row[i + 2] * 299) / 1000);
                            }
                        }
                    }
                }
                finally
                {
                    work.UnlockBits(d);
                }

                _pin = GCHandle.Alloc(gray, GCHandleType.Pinned);

                Image = new VA_Image();
                Image.data = _pin.AddrOfPinnedObject();
                Image.width = w;
                Image.height = h;
                Image.channels = 1;      // 交给算法库的一律是单通道灰度
                Image.stride = w;
            }

            public void Dispose()
            {
                if (_pin.IsAllocated) _pin.Free();
                if (_owned != null) { _owned.Dispose(); _owned = null; }
            }
        }

        /// <summary>创建带灰度调色板的 8bpp 位图并返回已锁定的 BitmapData。</summary>
        internal static Bitmap CreateGrayBitmap(int width, int height, out BitmapData data)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);
            ColorPalette palette = bmp.Palette;
            for (int i = 0; i < 256; i++)
                palette.Entries[i] = Color.FromArgb(255, i, i, i);
            bmp.Palette = palette;

            data = bmp.LockBits(new Rectangle(0, 0, width, height),
                                ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            return bmp;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.va_destroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    #region 托管结果类型

    public class PreprocessParam
    {
        public bool ToGray { get; set; }
        public int BlurKsize { get; set; }
        public int Threshold { get; set; }
        public bool Invert { get; set; }
        public int MorphOp { get; set; }
        public int MorphKsize { get; set; }

        public static PreprocessParam Default()
        {
            return new PreprocessParam
            {
                ToGray = true,
                BlurKsize = 3,
                Threshold = 128,
                Invert = false,
                MorphOp = 0,
                MorphKsize = 3
            };
        }

        internal VA_PreprocessParam ToNative()
        {
            VA_PreprocessParam p = new VA_PreprocessParam();
            p.to_gray = ToGray ? 1 : 0;
            p.blur_ksize = BlurKsize;
            p.threshold = Threshold;
            p.invert = Invert ? 1 : 0;
            p.morph_op = MorphOp;
            p.morph_ksize = MorphKsize;
            return p;
        }
    }

    public class BlobResult
    {
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double Area { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Angle { get; set; }

        internal static BlobResult FromNative(VA_Blob b)
        {
            return new BlobResult
            {
                Cx = b.cx, Cy = b.cy, Area = b.area,
                Width = b.width, Height = b.height, Angle = b.angle
            };
        }

        public override string ToString()
        {
            return string.Format("Blob ({0:F1},{1:F1}) {2:F0}x{3:F0} area={4:F0}",
                Cx, Cy, Width, Height, Area);
        }
    }

    public class CircleResult
    {
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double Radius { get; set; }
        public double Residual { get; set; }

        public CircleResult() { }
        public CircleResult(double cx, double cy, double radius, double residual)
        {
            Cx = cx; Cy = cy; Radius = radius; Residual = residual;
        }

        public override string ToString()
        {
            return string.Format("Circle ({0:F1},{1:F1}) R={2:F1}残差={3:F2}", Cx, Cy, Radius, Residual);
        }
    }

    public class LineResult
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }

        public LineResult() { }
        public LineResult(double x1, double y1, double x2, double y2)
        {
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        }

        public double Length
        {
            get { return Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1)); }
        }

        public override string ToString()
        {
            return string.Format("Line ({0:F1},{1:F1})->({2:F1},{3:F1}) len={4:F1}",
                X1, Y1, X2, Y2, Length);
        }
    }

    public class MatchResult
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Score { get; set; }

        public MatchResult() { }
        public MatchResult(double x, double y, double w, double h, double score)
        {
            X = x; Y = y; Width = w; Height = h; Score = score;
        }

        public override string ToString()
        {
            return string.Format("Match ({0:F1},{1:F1}) score={2:F3}", X, Y, Score);
        }
    }

    /// <summary>形状模型的概要信息。</summary>
    public class ShapeModelInfo
    {
        public int PointCount { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public ShapeModelInfo() { }

        public ShapeModelInfo(int pointCount, double width, double height)
        {
            PointCount = pointCount;
            Width = width;
            Height = height;
        }

        public bool IsEmpty { get { return PointCount <= 0; } }

        public override string ToString()
        {
            return string.Format("模板 {0:F0}x{1:F0}，边缘点 {2} 个", Width, Height, PointCount);
        }
    }

    /// <summary>单个形状匹配结果。(Cx, Cy) 为目标中心，Angle 单位为度。</summary>
    public class ShapeMatchResult
    {
        public double Cx { get; set; }
        public double Cy { get; set; }
        public double Angle { get; set; }
        public double Scale { get; set; }
        public double Score { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public ShapeMatchResult() { }

        internal static ShapeMatchResult FromNative(VA_ShapeMatch m)
        {
            return new ShapeMatchResult
            {
                Cx = m.cx, Cy = m.cy,
                Angle = m.angle, Scale = m.scale, Score = m.score,
                Width = m.width, Height = m.height
            };
        }

        /// <summary>以中心为原点、未旋转的外接矩形。</summary>
        public RectangleF Bounds
        {
            get
            {
                return new RectangleF((float)(Cx - Width / 2.0),
                                      (float)(Cy - Height / 2.0),
                                      (float)Width, (float)Height);
            }
        }

        public override string ToString()
        {
            return string.Format("({0:F1},{1:F1}) 角度 {2:F2}° 缩放 {3:F3} 得分 {4:F3}",
                Cx, Cy, Angle, Scale, Score);
        }
    }

    /// <summary>
    /// 模型特征点：模板边缘点的位置（相对模板中心）与单位梯度方向。
    /// 用于界面把「模板特征结构」画出来。
    /// </summary>
    public class ShapeModelPoint
    {
        /// <summary>相对模板中心的 X。</summary>
        public double X { get; set; }
        /// <summary>相对模板中心的 Y。</summary>
        public double Y { get; set; }
        /// <summary>单位梯度方向的 X 分量。</summary>
        public double Dx { get; set; }
        /// <summary>单位梯度方向的 Y 分量。</summary>
        public double Dy { get; set; }

        public ShapeModelPoint() { }

        internal static ShapeModelPoint FromNative(VA_ShapePoint p)
        {
            return new ShapeModelPoint { X = p.x, Y = p.y, Dx = p.dx, Dy = p.dy };
        }
    }

    /// <summary>
    /// 形状模型训练参数：控制特征点的数量与空间稀疏间距。
    /// 特征点越少越快；空间稀疏（网格去重）保证分布均匀，比按幅值等间隔抽取更稳。
    /// </summary>
    public class ShapeTrainParam
    {
        /// <summary>边缘提取低阈值（对比度），典型 20~60。</summary>
        public double Contrast { get; set; }

        /// <summary>最多保留的特征点数；越少越快（默认 1500）。</summary>
        public int MaxFeatures { get; set; }

        /// <summary>特征点最小间距（模板像素）；越大越稀疏、越快（默认 2.0）。</summary>
        public double MinFeatureDist { get; set; }

        public static ShapeTrainParam Default()
        {
            return new ShapeTrainParam
            {
                Contrast = 30.0,
                MaxFeatures = 1500,
                MinFeatureDist = 2.0
            };
        }

        internal VA_ShapeTrainParam ToNative()
        {
            VA_ShapeTrainParam p = new VA_ShapeTrainParam();
            p.contrast = Contrast;
            p.max_features = MaxFeatures;
            p.min_feature_dist = MinFeatureDist;
            return p;
        }

        public override string ToString()
        {
            return string.Format("对比度 {0:F0}，最多 {1} 点，间距 {2:F1} px",
                Contrast, MaxFeatures, MinFeatureDist);
        }
    }

    /// <summary>形状匹配参数。角度单位为度，缩放为相对模板尺寸的比例。</summary>
    public class ShapeParam
    {
        public double AngleStart { get; set; }
        public double AngleEnd { get; set; }
        public double AngleStep { get; set; }
        public double ScaleMin { get; set; }
        public double ScaleMax { get; set; }
        public double ScaleStep { get; set; }
        public int PyramidLevels { get; set; }
        public double MinScore { get; set; }
        public int MaxMatches { get; set; }
        public double MaxOverlap { get; set; }
        public bool Subpixel { get; set; }

        /// <summary>
        /// 贪婪系数 0~1，默认 0.9：越大越早淘汰差候选、越快，
        /// 但目标被严重遮挡时可能漏检；设 0 关闭该启发式（最慢最稳）。
        /// </summary>
        public double Greediness { get; set; }

        public static ShapeParam Default()
        {
            return new ShapeParam
            {
                AngleStart = -10.0,
                AngleEnd = 10.0,
                AngleStep = 1.0,
                ScaleMin = 1.0,
                ScaleMax = 1.0,
                ScaleStep = 0.0,        // 0 = 不搜索缩放
                PyramidLevels = 4,
                MinScore = 0.7,
                MaxMatches = 1,
                MaxOverlap = 0.5,
                Subpixel = true,
                Greediness = 0.9
            };
        }

        /// <summary>预估需要评估的姿态数（角度 × 缩放），用于给用户提示耗时量级。</summary>
        public int PoseCount
        {
            get
            {
                int a = 1;
                if (AngleStep > 0)
                {
                    a = (int)Math.Floor(Math.Abs(AngleEnd - AngleStart) / AngleStep + 1e-9) + 1;
                    if (a < 1) a = 1;
                }
                int s = 1;
                if (ScaleStep > 0 && ScaleMax > ScaleMin)
                {
                    s = (int)Math.Floor((ScaleMax - ScaleMin) / ScaleStep + 1e-9) + 1;
                    if (s < 1) s = 1;
                }
                return a * s;
            }
        }

        internal VA_ShapeParam ToNative()
        {
            VA_ShapeParam p = new VA_ShapeParam();
            p.angle_start = AngleStart;
            p.angle_end = AngleEnd;
            p.angle_step = AngleStep;
            p.scale_min = ScaleMin;
            p.scale_max = ScaleMax;
            p.scale_step = ScaleStep;
            p.pyramid_levels = PyramidLevels;
            p.min_score = MinScore;
            p.max_matches = MaxMatches;
            p.max_overlap = MaxOverlap;
            p.subpixel = Subpixel ? 1 : 0;
            p.greediness = Greediness;
            return p;
        }

        public override string ToString()
        {
            return string.Format("角度[{0}°~{1}°/{2}°] 缩放[{3}~{4}/{5}] 金字塔{6} 阈值{7:F2}",
                AngleStart, AngleEnd, AngleStep,
                ScaleMin, ScaleMax, ScaleStep,
                PyramidLevels, MinScore);
        }
    }

    #endregion
}
