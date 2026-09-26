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

            BitmapData srcData = null;
            Bitmap dst = null;
            BitmapData dstData = null;

            try
            {
                var rect = new Rectangle(0, 0, src.Width, src.Height);
                srcData = src.LockBits(rect, ImageLockMode.ReadOnly, src.PixelFormat);

                VA_Image srcImg = new VA_Image();
                srcImg.data = srcData.Scan0;
                srcImg.width = src.Width;
                srcImg.height = src.Height;
                srcImg.channels = ChannelsOf(src.PixelFormat);
                srcImg.stride = srcData.Stride;

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
                if (srcData != null) src.UnlockBits(srcData);
            }
        }

        /// <summary>连通域分析。输入可以是灰度图或二值图。</summary>
        public OpResult<List<BlobResult>> AnalyzeBlobs(Bitmap gray, double minArea, double maxArea = 0, int maxCount = 256)
        {
            if (gray == null) return OpResult<List<BlobResult>>.Fail("输入图像为空");

            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, gray.Width, gray.Height);
                data = gray.LockBits(rect, ImageLockMode.ReadOnly, gray.PixelFormat);

                VA_Image img = new VA_Image();
                img.data = data.Scan0;
                img.width = gray.Width;
                img.height = gray.Height;
                img.channels = ChannelsOf(gray.PixelFormat);
                img.stride = data.Stride;

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
            finally
            {
                if (data != null) gray.UnlockBits(data);
            }
        }

        /// <summary>清晰度评价：拉普拉斯方差，越大越清晰。</summary>
        public OpResult<double> Sharpness(Bitmap gray)
        {
            if (gray == null) return OpResult<double>.Fail("输入图像为空");

            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, gray.Width, gray.Height);
                data = gray.LockBits(rect, ImageLockMode.ReadOnly, gray.PixelFormat);

                VA_Image img = new VA_Image();
                img.data = data.Scan0;
                img.width = gray.Width;
                img.height = gray.Height;
                img.channels = ChannelsOf(gray.PixelFormat);
                img.stride = data.Stride;

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
            finally
            {
                if (data != null) gray.UnlockBits(data);
            }
        }

        /// <summary>对最大轮廓做圆拟合。</summary>
        public OpResult<CircleResult> FitCircle(Bitmap gray)
        {
            if (gray == null) return OpResult<CircleResult>.Fail("输入图像为空");

            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, gray.Width, gray.Height);
                data = gray.LockBits(rect, ImageLockMode.ReadOnly, gray.PixelFormat);

                VA_Image img = new VA_Image();
                img.data = data.Scan0;
                img.width = gray.Width;
                img.height = gray.Height;
                img.channels = ChannelsOf(gray.PixelFormat);
                img.stride = data.Stride;

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
            finally
            {
                if (data != null) gray.UnlockBits(data);
            }
        }

        /// <summary>对最大轮廓做直线拟合。</summary>
        public OpResult<LineResult> FitLine(Bitmap gray)
        {
            if (gray == null) return OpResult<LineResult>.Fail("输入图像为空");

            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, gray.Width, gray.Height);
                data = gray.LockBits(rect, ImageLockMode.ReadOnly, gray.PixelFormat);

                VA_Image img = new VA_Image();
                img.data = data.Scan0;
                img.width = gray.Width;
                img.height = gray.Height;
                img.channels = ChannelsOf(gray.PixelFormat);
                img.stride = data.Stride;

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
            finally
            {
                if (data != null) gray.UnlockBits(data);
            }
        }

        /// <summary>训练模板。roi 为空表示整图。</summary>
        public OpResult TrainTemplate(Bitmap gray, Rectangle roi)
        {
            if (gray == null) return OpResult.Fail("输入图像为空");

            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, gray.Width, gray.Height);
                data = gray.LockBits(rect, ImageLockMode.ReadOnly, gray.PixelFormat);

                VA_Image img = new VA_Image();
                img.data = data.Scan0;
                img.width = gray.Width;
                img.height = gray.Height;
                img.channels = ChannelsOf(gray.PixelFormat);
                img.stride = data.Stride;

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
            finally
            {
                if (data != null) gray.UnlockBits(data);
            }
        }

        /// <summary>模板匹配。</summary>
        public OpResult<MatchResult> MatchTemplate(Bitmap gray)
        {
            if (gray == null) return OpResult<MatchResult>.Fail("输入图像为空");

            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, gray.Width, gray.Height);
                data = gray.LockBits(rect, ImageLockMode.ReadOnly, gray.PixelFormat);

                VA_Image img = new VA_Image();
                img.data = data.Scan0;
                img.width = gray.Width;
                img.height = gray.Height;
                img.channels = ChannelsOf(gray.PixelFormat);
                img.stride = data.Stride;

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
            finally
            {
                if (data != null) gray.UnlockBits(data);
            }
        }

        /// <summary>沿指定行统计边缘跳变次数。</summary>
        public OpResult<int> EdgeCount(Bitmap gray, int scanRow, int delta = 30)
        {
            if (gray == null) return OpResult<int>.Fail("输入图像为空");

            BitmapData data = null;
            try
            {
                var rect = new Rectangle(0, 0, gray.Width, gray.Height);
                data = gray.LockBits(rect, ImageLockMode.ReadOnly, gray.PixelFormat);

                VA_Image img = new VA_Image();
                img.data = data.Scan0;
                img.width = gray.Width;
                img.height = gray.Height;
                img.channels = ChannelsOf(gray.PixelFormat);
                img.stride = data.Stride;

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
            finally
            {
                if (data != null) gray.UnlockBits(data);
            }
        }

        #endregion

        #region 辅助

        internal static int ChannelsOf(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.Format8bppIndexed: return 1;
                case PixelFormat.Format24bppRgb: return 3;
                case PixelFormat.Format32bppRgb:
                case PixelFormat.Format32bppArgb: return 4;
                default: return 3;
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

    #endregion
}
