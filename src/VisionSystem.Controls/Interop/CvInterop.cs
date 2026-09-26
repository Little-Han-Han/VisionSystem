using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VisionSystem.Controls.Interop
{
    /// <summary>
    /// OpenCV 像素在内存中的排布方式，对应常见的 <c>CV_8U</c> 系列 Mat 类型。
    /// </summary>
    public enum CvPixelLayout
    {
        /// <summary>单通道灰度，对应 <c>CV_8UC1</c>。</summary>
        Gray8 = 0,

        /// <summary>三通道 B-G-R（OpenCV 默认顺序），对应 <c>CV_8UC3</c>。</summary>
        Bgr24 = 1,

        /// <summary>三通道 R-G-B，对应已做过 <c>cvtColor(COLOR_BGR2RGB)</c> 的 <c>CV_8UC3</c>。</summary>
        Rgb24 = 2,

        /// <summary>四通道 B-G-R-A，对应 <c>CV_8UC4</c>。</summary>
        Bgra32 = 3
    }

    /// <summary>
    /// 与 OpenCV 的数据互操作层。<strong>不依赖任何第三方 NuGet 包</strong>，
    /// 直接基于 <c>Mat.data</c> 指针（<see cref="IntPtr"/>）与行字节数（stride / <c>Mat.step</c>）工作。
    /// </summary>
    /// <remarks>
    /// <para>OpenCvSharp 用法：</para>
    /// <code>
    /// using (var mat = Cv2.ImRead("a.jpg", ImreadModes.Color))
    /// {
    ///     var bmp = CvInterop.ToBitmap(mat.Data, mat.Width, mat.Height, (int)mat.Step(), CvPixelLayout.Bgr24);
    ///     imageViewer1.SetImage(bmp);
    /// }
    /// </code>
    /// <para>cv::Mat（C++ CLI）用法：</para>
    /// <code>
    /// var bmp = CvInterop.ToBitmap((IntPtr)mat.data, mat.cols, mat.rows, (int)mat.step, CvPixelLayout.Bgr24);
    /// </code>
    /// </remarks>
    public static class CvInterop
    {
        /// <summary>每种排布对应的每像素字节数。</summary>
        public static int GetBytesPerPixel(CvPixelLayout layout)
        {
            switch (layout)
            {
                case CvPixelLayout.Gray8: return 1;
                case CvPixelLayout.Bgr24:
                case CvPixelLayout.Rgb24: return 3;
                case CvPixelLayout.Bgra32: return 4;
                default: throw new ArgumentOutOfRangeException("layout");
            }
        }

        /// <summary>每种排布对应的 GDI+ 像素格式。</summary>
        public static PixelFormat GetPixelFormat(CvPixelLayout layout)
        {
            switch (layout)
            {
                case CvPixelLayout.Gray8: return PixelFormat.Format8bppIndexed;
                case CvPixelLayout.Bgr24:
                case CvPixelLayout.Rgb24: return PixelFormat.Format24bppRgb;
                case CvPixelLayout.Bgra32: return PixelFormat.Format32bppArgb;
                default: throw new ArgumentOutOfRangeException("layout");
            }
        }

        /// <summary>
        /// 把 OpenCV 的像素缓冲区转换为 <see cref="Bitmap"/>（深拷贝，转换后可立即释放 Mat）。
        /// </summary>
        /// <param name="data">Mat 首像素指针（OpenCvSharp 为 <c>mat.Data</c>）。</param>
        /// <param name="width">图像宽度（<c>mat.Cols</c>）。</param>
        /// <param name="height">图像高度（<c>mat.Rows</c>）。</param>
        /// <param name="stride">每行字节数（<c>(int)mat.Step()</c>），可包含行尾填充。</param>
        /// <param name="layout">像素排布。</param>
        /// <returns>新的 <see cref="Bitmap"/>，调用方负责释放。</returns>
        public static Bitmap ToBitmap(IntPtr data, int width, int height, int stride, CvPixelLayout layout)
        {
            if (data == IntPtr.Zero) throw new ArgumentNullException("data");
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException("width");

            int bpp = GetBytesPerPixel(layout);
            int minStride = width * bpp;
            if (stride <= 0) stride = minStride;
            if (stride < minStride) throw new ArgumentException(
                "stride 小于一行像素所需的最小字节数，请检查 Mat 的类型与通道数是否匹配。", "stride");

            PixelFormat format = GetPixelFormat(layout);
            var bmp = new Bitmap(width, height, format);

            if (layout == CvPixelLayout.Gray8)
            {
                // GDI+ 的 8bpp 位图必须附带灰度调色板，否则显示为纯黑
                ColorPalette palette = bmp.Palette;
                for (int i = 0; i < 256; i++)
                    palette.Entries[i] = Color.FromArgb(255, i, i, i);
                bmp.Palette = palette;
            }

            var rect = new Rectangle(0, 0, width, height);
            BitmapData bd = bmp.LockBits(rect, ImageLockMode.WriteOnly, format);
            try
            {
                byte[] row = new byte[minStride];
                for (int y = 0; y < height; y++)
                {
                    IntPtr src = IntPtr.Add(data, y * stride);
                    IntPtr dst = IntPtr.Add(bd.Scan0, y * bd.Stride);

                    Marshal.Copy(src, row, 0, minStride);

                    if (layout == CvPixelLayout.Bgr24)
                    {
                        // BGR -> RGB
                        for (int x = 0; x < width; x++)
                        {
                            byte b = row[x * 3];
                            byte g = row[x * 3 + 1];
                            byte r = row[x * 3 + 2];
                            row[x * 3] = r;
                            row[x * 3 + 1] = g;
                            row[x * 3 + 2] = b;
                        }
                    }
                    else if (layout == CvPixelLayout.Bgra32)
                    {
                        // OpenCV 的 CV_8UC4 第 4 通道通常无意义，强制置为不透明
                        for (int x = 0; x < width; x++)
                            row[x * 4 + 3] = 255;
                    }

                    Marshal.Copy(row, 0, dst, minStride);
                }
            }
            finally
            {
                bmp.UnlockBits(bd);
            }

            return bmp;
        }

        /// <summary>
        /// 把托管的连续像素数组转换为 <see cref="Bitmap"/>（深拷贝）。
        /// 适用于数据来自网络流、文件或其他非 OpenCV 来源的场景。
        /// </summary>
        public static Bitmap FromPixels(byte[] pixels, int width, int height, CvPixelLayout layout)
        {
            if (pixels == null) throw new ArgumentNullException("pixels");
            int bpp = GetBytesPerPixel(layout);
            if (pixels.Length < width * height * bpp)
                throw new ArgumentException("像素缓冲区长度不足以填满指定尺寸的图像。", "pixels");

            GCHandle handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                return ToBitmap(handle.AddrOfPinnedObject(), width, height, width * bpp, layout);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// 把 <see cref="Bitmap"/> 导出为连续像素数组（<c>CV_8UC3</c> 的 BGR 排布），
        /// 可直接喂给 <c>new Mat(rows, cols, MatType.CV_8UC3, bytes)</c>。
        /// </summary>
        public static byte[] ToBgrBytes(Bitmap bmp)
        {
            return ToBytes(bmp, CvPixelLayout.Bgr24);
        }

        /// <summary>把 <see cref="Bitmap"/> 导出为连续灰度数组（<c>CV_8UC1</c>）。</summary>
        public static byte[] ToGrayBytes(Bitmap bmp)
        {
            return ToBytes(bmp, CvPixelLayout.Gray8);
        }

        /// <summary>把 <see cref="Bitmap"/> 导出为指定排布的连续像素数组。</summary>
        public static byte[] ToBytes(Bitmap bmp, CvPixelLayout layout)
        {
            if (bmp == null) throw new ArgumentNullException("bmp");

            int width = bmp.Width;
            int height = bmp.Height;
            int bpp = GetBytesPerPixel(layout);
            int minStride = width * bpp;
            var result = new byte[minStride * height];

            // 统一先转成 24bpp RGB 再做通道/灰度处理，避免处理各种源格式的索引与 alpha
            using (var tmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(tmp))
                {
                    g.DrawImage(bmp, 0, 0, width, height);
                }

                var rect = new Rectangle(0, 0, width, height);
                BitmapData bd = tmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    byte[] row = new byte[width * 3];
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr src = IntPtr.Add(bd.Scan0, y * bd.Stride);
                        Marshal.Copy(src, row, 0, width * 3);

                        int dst = y * minStride;
                        for (int x = 0; x < width; x++)
                        {
                            byte b = row[x * 3];
                            byte g = row[x * 3 + 1];
                            byte r = row[x * 3 + 2];

                            switch (layout)
                            {
                                case CvPixelLayout.Bgr24:
                                    result[dst + x * 3] = b;
                                    result[dst + x * 3 + 1] = g;
                                    result[dst + x * 3 + 2] = r;
                                    break;
                                case CvPixelLayout.Rgb24:
                                    result[dst + x * 3] = r;
                                    result[dst + x * 3 + 1] = g;
                                    result[dst + x * 3 + 2] = b;
                                    break;
                                case CvPixelLayout.Gray8:
                                    // 与 OpenCV COLOR_RGB2GRAY 相同的权重
                                    result[dst + x] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                                    break;
                                case CvPixelLayout.Bgra32:
                                    result[dst + x * 4] = b;
                                    result[dst + x * 4 + 1] = g;
                                    result[dst + x * 4 + 2] = r;
                                    result[dst + x * 4 + 3] = 255;
                                    break;
                            }
                        }
                    }
                }
                finally
                {
                    tmp.UnlockBits(bd);
                }
            }

            return result;
        }

        /// <summary>
        /// 把几何图形转换为 OpenCV 可直接使用的参数结构说明文本（便于日志/调试）。
        /// 例如旋转矩形输出 <c>center/size/angle</c> 三元组。
        /// </summary>
        public static string ToCvParameterString(Geometry.IGeometryShape shape)
        {
            if (shape == null) return string.Empty;

            var rotated = shape as Geometry.RotatedRectShape;
            if (rotated != null)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "RotatedRect(center=({0:F3},{1:F3}), size=({2:F3},{3:F3}), angle={4:F3})",
                    rotated.Center.X, rotated.Center.Y, rotated.Width, rotated.Height, rotated.AngleDeg);
            }

            var circle = shape as Geometry.CircleShape;
            if (circle != null)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Circle(center=({0:F3},{1:F3}), radius={2:F3})",
                    circle.Center.X, circle.Center.Y, circle.Radius);
            }

            var rect = shape as Geometry.RectangleShape;
            if (rect != null)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Rect(x={0:F3}, y={1:F3}, w={2:F3}, h={3:F3})",
                    rect.X, rect.Y, rect.Width, rect.Height);
            }

            var ring = shape as Geometry.RingShape;
            if (ring != null)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Ring(center=({0:F3},{1:F3}), innerRadius={2:F3}, outerRadius={3:F3})",
                    ring.Center.X, ring.Center.Y, ring.InnerRadius, ring.OuterRadius);
            }

            return shape.ToGeometryString();
        }
    }
}
