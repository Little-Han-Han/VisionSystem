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

    /// <summary>
    /// 滤波参数（对应 C 端 VA_FilterParam，见 include/algo/va_filter.h）。
    /// 字段顺序 / 类型与 C 端严格一一对应，改动必须同步改头文件。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_FilterParam
    {
        public int    type;         /* 滤波类型：见 FilterType */
        public int    ksize;        /* 核边长；原生会修正为 >=3 的奇数 */
        public double sigma;        /* 高斯 / 双边的 sigma；<=0 时自动推导 */
        public double amount;       /* 锐化强度（USM），典型 1.0；0 = 不锐化 */
        public int    morph_op;     /* 形态学算子：见 FilterMorphOp */
        public int    morph_shape;  /* 结构化元素形状：见 MorphShape */
        public int    morph_ksize;  /* 形态学核边长；<3 则不做形态学 */
        public int    iterations;   /* 形态学迭代次数；<1 按 1 */
    }

    /// <summary>
    /// 颜色提取参数（对应 C 端 VA_ColorParam，见 include/algo/va_color.h）。
    /// low / high 是 4 元定长数组，必须用 ByValArray 固定长度，否则内存布局与 C 端不一致。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_ColorParam
    {
        public int space;       /* 颜色空间：0 = BGR，1 = HSV */
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public int[] low;       /* 下界：B/H, G/S, R/V, (未用) */
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public int[] high;      /* 上界：B/H, G/S, R/V, (未用) */
        public int morph_op;    /* 形态学清理算子：0 无 / 1 开 / 2 闭 */
        public int morph_ksize; /* 形态学核边长；<3 则不做 */
    }

    /// <summary>颜色提取的命中统计（对应 C 端 VA_ColorStat）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_ColorStat
    {
        public double pixel_count;             /* 命中像素数 */
        public double ratio;                   /* 命中像素 / 总像素，0~1 */
        public double mean_b, mean_g, mean_r;  /* 命中区域内的平均颜色（BGR 字节序） */
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

    /// <summary>
    /// 卡尺参数（对应 C 端 VA_CaliperParam）。字段顺序、类型与 C 端严格一一对应：
    /// 直线模式只用 x1/y1/x2/y2，圆模式只用 cx/cy/inner_radius/outer_radius/start_angle/angle_span。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_CaliperParam
    {
        public double x1;
        public double y1;
        public double x2;
        public double y2;
        public double cx;
        public double cy;
        public double inner_radius;
        public double outer_radius;
        public double start_angle;
        public double angle_span;
        public int    caliper_count;
        public double projection_length;
        public int    polarity;
        public double edge_threshold;
        public int    filter_half_width;
        public int    select_mode;
        public int    min_edge_count;
        public int    reject_outliers;
        public double outlier_tolerance;
    }

    /// <summary>
    /// 单个卡尺的输出（对应 C 端 VA_CaliperPoint）。
    /// px1/py1/px2/py2 为投影窗口两端（未命中也有效，供可视化）；
    /// x/y 为亚像素命中点，valid=1 表示命中。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_CaliperPoint
    {
        public double px1;
        public double py1;
        public double px2;
        public double py2;
        public double x;
        public double y;
        public double strength;
        public int    valid;
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

    /// <summary>
    /// 单个目标检测结果（对应 C 端 VA_DetectResult，见 include/algo/va_detect.h）。
    /// 字段顺序 / 类型与 C 端严格一一对应：4 个 float（左上角 x,y + 宽高 w,h）→
    /// int class_id → float score。改动必须同步改 C 端头文件。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_DetectResult
    {
        public float x;          /* 左上角 x（原图像素坐标） */
        public float y;          /* 左上角 y */
        public float w;          /* 宽 */
        public float h;          /* 高 */
        public int   class_id;   /* 类别索引 */
        public float score;      /* 置信度 0~1 */
    }

    /// <summary>
    /// OCR 参数（对应 C 端 VA_OcrParam，见 include/algo/va_ocr.h）。
    /// 字段顺序 / 类型与 C 端严格一一对应，改动必须同步改 C 端头文件。
    /// 哨兵语义：threshold&lt;0 → Otsu 自适应阈值；min/max_char_h 与 min/max_aspect &lt;=0 → 不限制；
    /// max_gap&lt;0 → 按 3 处理；norm_w / norm_h &lt;=0 → 16 / 24。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_OcrParam
    {
        public int    threshold;    /* 二值化阈值；<0 自动 Otsu */
        public int    invert;       /* 1 = 极性反转（白底黑字）*/
        public double min_char_h;   /* 字符最小高度（像素）；<=0 不限制 */
        public double max_char_h;   /* 字符最大高度（像素）；<=0 不限制 */
        public double min_aspect;   /* 最小宽高比 w/h；<=0 不限制 */
        public double max_aspect;   /* 最大宽高比 w/h；<=0 不限制 */
        public int    max_gap;      /* 同行相邻字符最大间隙（像素）；<0 按 3 */
        public int    norm_w;       /* 归一化宽；<=0 按 16 */
        public int    norm_h;       /* 归一化高；<=0 按 24 */
        public double accept_score; /* 匹配接受阈值 0~1，低于该值输出 '?' */
    }

    /// <summary>
    /// 单个字符（对应 C 端 VA_OcrChar）。label 为 UTF-8 定长 8 字节（VA_OCR_LABEL_BYTES），
    /// 支持中文标签（如"中"占 3 字节）。va_ocr_recognize_ex 会填 label / score，
    /// va_ocr_segment 只填 x/y/w/h（label 置 "?"、score 置 0）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_OcrChar
    {
        public double x;
        public double y;
        public double w;
        public double h;
        /// <summary>与 C 端 VA_OCR_LABEL_BYTES（8）一致：UTF-8 定长标签。</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] label;
        public double score;
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
        public const int ErrTimeout = 7;
        public const int ErrCamera = 8;
        public const int ErrNoEdge = 9;

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
                case ErrTimeout: return "读帧超时";
                case ErrCamera: return "相机错误";
                case ErrNoEdge: return "有效边缘点不足";
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

        // ---- 图像滤波 / 颜色提取（3 通道彩色链路）----
        //
        // C 端这两个函数的图像输出参数都允许传 NULL（dst / dst_mask / out_blobs / out_stat）。
        // C# 的 ref 不能表达 NULL，因此「不要掩膜」单独起一个别名声明（EntryPoint 指回同一个导出名），
        // 用 IntPtr.Zero 显式传空指针，避免为了凑 ref 而白算一张不需要的图。

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_filter(IntPtr handle, ref VA_Image src,
                                           ref VA_Image dst, ref VA_FilterParam param);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_color_extract(IntPtr handle, ref VA_Image src,
                                                  ref VA_Image dst_mask,
                                                  ref VA_ColorParam param,
                                                  double min_area, double max_area,
                                                  [In, Out] VA_Blob[] out_blobs,
                                                  int max_count, out int out_count,
                                                  ref VA_ColorStat out_stat);

        /// <summary>
        /// <see cref="va_color_extract"/> 的「不要掩膜」版本：dst_mask 固定传 NULL，
        /// 其余参数逐一对应。只要统计与 Blob、不想付掩膜的分配与拷贝开销时用这个。
        /// </summary>
        [DllImport(Dll, EntryPoint = "va_color_extract", CallingConvention = CallingConvention.StdCall)]
        public static extern int va_color_extract_nomask(IntPtr handle, ref VA_Image src,
                                                         IntPtr dst_mask,
                                                         ref VA_ColorParam param,
                                                         double min_area, double max_area,
                                                         [In, Out] VA_Blob[] out_blobs,
                                                         int max_count, out int out_count,
                                                         ref VA_ColorStat out_stat);

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

        // ---- 卡尺（直线 / 圆）----

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_line_caliper(IntPtr handle, ref VA_Image gray,
                                                 ref VA_CaliperParam param,
                                                 [In, Out] VA_CaliperPoint[] out_points,
                                                 int max_count, out int out_count,
                                                 out VA_Line out_line, out double out_rms);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_circle_caliper(IntPtr handle, ref VA_Image gray,
                                                   ref VA_CaliperParam param,
                                                   [In, Out] VA_CaliperPoint[] out_points,
                                                   int max_count, out int out_count,
                                                   out VA_Circle out_circle, out double out_rms);

        // ---- 目标识别（OpenVINO YOLO，va_det_*）----
        // 字符串参数（模型路径 / 设备名）按 UTF-8 字节传，避免默认 ANSI 封送破坏中文路径。

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_det_create(IntPtr handle, byte[] model_path, byte[] device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_det_run(IntPtr handle, ref VA_Image img,
                                           float score_thresh, float iou_thresh,
                                           [In, Out] VA_DetectResult[] out_results,
                                           int max_count, out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_det_class_count(IntPtr handle, out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_det_class_name(IntPtr handle, int idx, byte[] out_buf, int buf_size);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_det_clear(IntPtr handle);

        // ---- 字符识别（传统算法 OCR，va_ocr_*）----
        // 标签字符串按 UTF-8 字节传，避免默认 ANSI 封送破坏中文标签（如"中.png"）。

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_ocr_set_param(IntPtr handle, ref VA_OcrParam param);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_ocr_add_template(IntPtr handle, ref VA_Image gray, byte[] label);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_ocr_template_count(IntPtr handle, out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_ocr_clear_templates(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_ocr_segment(IntPtr handle, ref VA_Image gray,
                                                ref VA_OcrParam param,
                                                [In, Out] VA_OcrChar[] out_chars,
                                                int max_count, out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_ocr_recognize(IntPtr handle, ref VA_Image gray,
                                                  ref VA_OcrParam param,
                                                  byte[] out_text, int text_cap,
                                                  out double out_mean_score);

        // 分割 + 识别一次调用：文本与每个字符的标签 / 置信度同源输出。
        // out_chars 传 null 表示只要文本；out_text 传 null 表示只要字符。
        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_ocr_recognize_ex(IntPtr handle, ref VA_Image gray,
                                                     ref VA_OcrParam param,
                                                     [In, Out] VA_OcrChar[] out_chars,
                                                     int chars_cap, out int out_char_count,
                                                     byte[] out_text, int text_cap,
                                                     out double out_mean_score);
    }

    /// <summary>
    /// C++ 算法库（VisionAlgo.Native.dll）的托管封装。
    /// 每个实例对应库内的一个上下文句柄，模板训练结果保存在该上下文中。
    /// </summary>
    public class VisionAlgo : IDisposable
    {
        private IntPtr _handle = IntPtr.Zero;
        private bool _disposed;

        /// <summary>已加载的检测模型路径缓存：同一路径不重复编译，避免连续运行每帧重建模型。</summary>
        private string _detModelPath = "";

        /// <summary>已加载的检测设备缓存（与 <see cref="_detModelPath"/> 一起决定是否需要重载）。</summary>
        private string _detDevice = "";

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

        // ---------------------- 图像滤波 / 颜色提取 ----------------------

        /// <summary>
        /// 图像滤波：平滑 / 锐化 + 可选形态学。
        /// <para>
        /// 通道数<strong>进出一致</strong>：<paramref name="param"/>.KeepColor 为真且输入是
        /// 24bpp / 32bpp 彩色图时走 3 通道（BGR）链路，输入输出都是 24bpp；
        /// 否则走既有灰度链路（输入输出都是 8bpp 灰度）。返回值可直接作为下游工作帧。
        /// </para>
        /// </summary>
        public OpResult<Bitmap> Filter(Bitmap src, FilterParam param)
        {
            if (src == null) return OpResult<Bitmap>.Fail("输入图像为空");
            if (param == null) param = FilterParam.Default();

            VA_FilterParam p = param.ToNative();
            return (param.KeepColor && IsColorFormat(src.PixelFormat))
                ? FilterOnColor(src, p)
                : FilterOnGray(src, p);
        }

        private OpResult<Bitmap> FilterOnGray(Bitmap src, VA_FilterParam p)
        {
            // 与既有算法一致：彩色图按 Rec.601 亮度公式转 8bpp 灰度后再交给算法库
            using (var gi = new GrayInput(src))
            {
                Bitmap dst = null;
                BitmapData dstData = null;
                bool ok = false;

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

                    int rc = NativeMethods.va_filter(_handle, ref srcImg, ref dstImg, ref p);
                    if (rc != VaResult.OK)
                        return OpResult<Bitmap>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    ok = true;
                    return OpResult<Bitmap>.Ok(dst);
                }
                catch (Exception ex)
                {
                    return OpResult<Bitmap>.Fail("图像滤波失败：" + ex.Message);
                }
                finally
                {
                    // 顺序：先解锁（位图还活着），失败路径再释放，避免对已释放位图 UnlockBits
                    if (dstData != null && dst != null) dst.UnlockBits(dstData);
                    if (!ok && dst != null) dst.Dispose();
                }
            }
        }

        private OpResult<Bitmap> FilterOnColor(Bitmap src, VA_FilterParam p)
        {
            using (var ci = new ColorInput(src))
            {
                Bitmap dst = null;
                BitmapData dstData = null;
                bool ok = false;

                try
                {
                    VA_Image srcImg = ci.Image;

                    dst = CreateColorBitmap(src.Width, src.Height, out dstData);

                    VA_Image dstImg = new VA_Image();
                    dstImg.data = dstData.Scan0;
                    dstImg.width = dst.Width;
                    dstImg.height = dst.Height;
                    dstImg.channels = 3;
                    dstImg.stride = dstData.Stride;

                    int rc = NativeMethods.va_filter(_handle, ref srcImg, ref dstImg, ref p);
                    if (rc != VaResult.OK)
                        return OpResult<Bitmap>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    ok = true;
                    return OpResult<Bitmap>.Ok(dst);
                }
                catch (Exception ex)
                {
                    return OpResult<Bitmap>.Fail("图像滤波失败：" + ex.Message);
                }
                finally
                {
                    if (dstData != null && dst != null) dst.UnlockBits(dstData);
                    if (!ok && dst != null) dst.Dispose();
                }
            }
        }

        /// <summary>
        /// 颜色提取：在 BGR / HSV 空间做阈值分割 → 形态学清理 → 连通域统计。
        /// <para>
        /// <strong>必须使用彩色输入</strong>（24bpp / 32bpp）；传入灰度图时
        /// <see cref="ColorInput"/> 也会把它按三通道复制成 BGR，但那样阈值就没有意义了，
        /// 因此调用方（FlowRunner）会在无彩色帧时直接判失败并给出中文提示。
        /// </para>
        /// <para>
        /// <paramref name="param"/>.OutputMask 为 false 时不向原生要掩膜，
        /// 返回结果的 <see cref="ColorExtractResult.Mask"/> 为 null。
        /// </para>
        /// </summary>
        public OpResult<ColorExtractResult> ExtractColor(Bitmap src, ColorExtractParam param)
        {
            if (src == null) return OpResult<ColorExtractResult>.Fail("输入图像为空");
            if (param == null) param = ColorExtractParam.Default();

            using (var ci = new ColorInput(src))
            {
                Bitmap mask = null;
                BitmapData maskData = null;
                bool ok = false;

                try
                {
                    VA_Image srcImg = ci.Image;
                    VA_ColorParam p = param.ToNative();

                    // OutputMask 为假时跳过掩膜缓冲：省一次全图分配与逐行拷贝
                    VA_Image maskImg = new VA_Image();
                    if (param.OutputMask)
                    {
                        mask = CreateGrayBitmap(src.Width, src.Height, out maskData);
                        maskImg.data = maskData.Scan0;
                        maskImg.width = mask.Width;
                        maskImg.height = mask.Height;
                        maskImg.channels = 1;
                        maskImg.stride = maskData.Stride;
                    }

                    int maxCount = param.MaxCount > 0 ? param.MaxCount : 1;
                    VA_Blob[] raw = new VA_Blob[maxCount];
                    int count = 0;
                    VA_ColorStat stat = new VA_ColorStat();

                    int rc = param.OutputMask
                        ? NativeMethods.va_color_extract(_handle, ref srcImg, ref maskImg, ref p,
                                                         param.MinArea, param.MaxArea,
                                                         raw, maxCount, out count, ref stat)
                        : NativeMethods.va_color_extract_nomask(_handle, ref srcImg, IntPtr.Zero, ref p,
                                                                param.MinArea, param.MaxArea,
                                                                raw, maxCount, out count, ref stat);

                    if (rc != VaResult.OK)
                        return OpResult<ColorExtractResult>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    var result = new ColorExtractResult();
                    result.Mask = mask;
                    result.PixelCount = stat.pixel_count;
                    result.Ratio = stat.ratio;
                    result.MeanB = stat.mean_b;
                    result.MeanG = stat.mean_g;
                    result.MeanR = stat.mean_r;

                    for (int i = 0; i < count && i < raw.Length; i++)
                        result.Blobs.Add(BlobResult.FromNative(raw[i]));

                    ok = true;
                    return OpResult<ColorExtractResult>.Ok(result);
                }
                catch (Exception ex)
                {
                    return OpResult<ColorExtractResult>.Fail("颜色提取失败：" + ex.Message);
                }
                finally
                {
                    if (maskData != null && mask != null) mask.UnlockBits(maskData);
                    if (!ok && mask != null) mask.Dispose();
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

        // ---------------------- 卡尺（直线 / 圆）----------------------

        /// <summary>
        /// 直线卡尺：沿扫描线段法向等分投放投影卡尺找边，最小二乘拟合直线。
        /// 返回命中点（含未命中的窗口，供可视化）、拟合线段、直线度残差 RMS。
        /// </summary>
        public OpResult<CaliperLineResult> LineCaliper(Bitmap gray, CaliperParam param)
        {
            if (gray == null) return OpResult<CaliperLineResult>.Fail("输入图像为空");
            if (param == null) param = CaliperParam.LineDefault();

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                    VA_Image img = gi.Image;
                    VA_CaliperParam p = param.ToNative();

                    int count = param.CaliperCount > 0 ? param.CaliperCount : 1;
                    VA_CaliperPoint[] raw = new VA_CaliperPoint[count];
                    int outCount = 0;
                    VA_Line line;
                    double rms;

                    int rc = NativeMethods.va_line_caliper(_handle, ref img, ref p,
                                                           raw, count, out outCount,
                                                           out line, out rms);
                    if (rc != VaResult.OK)
                        return OpResult<CaliperLineResult>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    var result = new CaliperLineResult();
                    result.Points = ToCaliperPoints(raw, outCount);
                    result.Line = new LineResult(line.x1, line.y1, line.x2, line.y2);
                    result.Rms = rms;
                    return OpResult<CaliperLineResult>.Ok(result);
                }
                catch (Exception ex)
                {
                    return OpResult<CaliperLineResult>.Fail("直线卡尺失败：" + ex.Message);
                }
            }
        }

        /// <summary>
        /// 圆卡尺：沿圆周等分投放径向卡尺找边，代数最小二乘拟合圆。
        /// 返回命中点（含未命中的窗口）、圆心 / 半径 / 直径、圆度残差 RMS。
        /// </summary>
        public OpResult<CaliperCircleResult> CircleCaliper(Bitmap gray, CaliperParam param)
        {
            if (gray == null) return OpResult<CaliperCircleResult>.Fail("输入图像为空");
            if (param == null) param = CaliperParam.CircleDefault();

            // 统一转成 8bpp 灰度再交给算法库（彩色图按 Rec.601 亮度公式转换）
            using (var gi = new GrayInput(gray))
            {
                try
                {
                    VA_Image img = gi.Image;
                    VA_CaliperParam p = param.ToNative();

                    int count = param.CaliperCount > 0 ? param.CaliperCount : 1;
                    VA_CaliperPoint[] raw = new VA_CaliperPoint[count];
                    int outCount = 0;
                    VA_Circle circle;
                    double rms;

                    int rc = NativeMethods.va_circle_caliper(_handle, ref img, ref p,
                                                             raw, count, out outCount,
                                                             out circle, out rms);
                    if (rc != VaResult.OK)
                        return OpResult<CaliperCircleResult>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    var result = new CaliperCircleResult();
                    result.Points = ToCaliperPoints(raw, outCount);
                    result.Circle = new CircleResult(circle.cx, circle.cy, circle.radius, circle.residual);
                    result.Rms = rms;
                    return OpResult<CaliperCircleResult>.Ok(result);
                }
                catch (Exception ex)
                {
                    return OpResult<CaliperCircleResult>.Fail("圆卡尺失败：" + ex.Message);
                }
            }
        }

        /// <summary>
        /// 目标识别（OpenVINO YOLO）：对彩色帧做深度学习目标检测。
        /// 模型按路径缓存，同一路径不重复编译；路径 / 设备变化时自动重载。
        /// <para>输入优先用彩色帧（<see cref="ColorInput"/> 同款链路），灰度帧也会自动转 BGR 后推理。</para>
        /// </summary>
        // ---------------------- 深度学习（分割 / 姿态 / 深度）---------------------

        private readonly HashSet<string> _dlReady = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>加载模型（按"任务|路径|设备"缓存，避免连续运行时每帧重编译）。</summary>
        private string DlEnsure(int task, string modelPath, string device, out string error)
        {
            error = "";
            string mp = ResolveModelPath(modelPath);
            if (string.IsNullOrWhiteSpace(mp)) { error = "未指定模型路径"; return null; }
            if (!System.IO.File.Exists(mp)) { error = "模型文件不存在：" + mp; return null; }

            string dev = string.IsNullOrEmpty(device) ? "CPU" : device;
            string key = task + "|" + mp + "|" + dev;
            if (_dlReady.Contains(key)) return mp;

            int rc = task == NativeDl.TaskSeg ? NativeDl.SegCreate(_handle, mp, dev)
                   : task == NativeDl.TaskPose ? NativeDl.PoseCreate(_handle, mp, dev)
                   : NativeDl.DepthCreate(_handle, mp, dev);

            if (rc != 0)
            {
                error = (string.Equals(dev, "CUDA", StringComparison.OrdinalIgnoreCase)
                        ? "CUDA 推理尚未实现（接口已预留），请先用 CPU/GPU"
                        : VaResult.ToString(rc) + "：" + LastError);
                return null;
            }

            _dlReady.Add(key);
            return mp;
        }

        /// <summary>分割/姿态的统一输出：目标列表 + 可选的分割标签图。</summary>
        public sealed class DlResult
        {
            public List<NativeDl.DlTarget> Targets = new List<NativeDl.DlTarget>();
            public byte[] LabelMap;
            public int MapW;
            public int MapH;
        }

        /// <summary>语义分割 / 姿态估计（共用一套目标结构）。</summary>
        public OpResult<DlResult> DlDetect(int task, Bitmap src,
                                           float scoreThresh, float iouThresh,
                                           int maxCount, string modelPath, string device)
        {
            if (src == null) return OpResult<DlResult>.Fail("输入图像为空");

            string err;
            string mp = DlEnsure(task, modelPath, device, out err);
            if (mp == null) return OpResult<DlResult>.Fail(err);

            using (var ci = new ColorInput(src))
            {
                try
                {
                    VA_Image img = ci.Image;
                    var output = new DlResult();

                    if (task == NativeDl.TaskSeg)
                    {
                        NativeDl.SegResult seg = NativeDl.SegRun(_handle, ref img, scoreThresh, iouThresh,
                                                                 maxCount, out err);
                        if (!string.IsNullOrEmpty(err)) return OpResult<DlResult>.Fail(err);

                        output.Targets = seg.Targets;
                        output.LabelMap = seg.LabelMap;
                        output.MapW = seg.MapW;
                        output.MapH = seg.MapH;
                    }
                    else
                    {
                        List<NativeDl.DlTarget> pose = NativeDl.PoseRun(_handle, ref img, scoreThresh, iouThresh,
                                                                        maxCount, out err);
                        if (!string.IsNullOrEmpty(err)) return OpResult<DlResult>.Fail(err);
                        output.Targets = pose;
                    }

                    return OpResult<DlResult>.Ok(output);
                }
                catch (Exception ex)
                {
                    return OpResult<DlResult>.Fail("推理异常：" + ex.Message);
                }
            }
        }

        // ---------------------- 深度学习 OCR（PaddleOCR）---------------------

        private string _ocrDlKey = "";

        /// <summary>加载 PaddleOCR 两个模型 + 字典（按 det|rec|dict|device 缓存）。det 可为空。</summary>
        private string OcrDlEnsure(string det, string rec, string dict, string device, out string error)
        {
            error = "";

            string d1 = string.IsNullOrWhiteSpace(det) ? "" : ResolveModelPath(det);
            string d2 = ResolveModelPath(rec);
            string d3 = ResolveModelPath(dict);
            if (!string.IsNullOrWhiteSpace(d1) && !System.IO.File.Exists(d1)) { error = "检测模型不存在：" + d1; return null; }
            if (string.IsNullOrWhiteSpace(d2) || !System.IO.File.Exists(d2)) { error = "识别模型不存在：" + d2; return null; }
            if (string.IsNullOrWhiteSpace(d3) || !System.IO.File.Exists(d3)) { error = "字典文件不存在：" + d3; return null; }

            string dev = string.IsNullOrEmpty(device) ? "CPU" : device;
            string key = d1 + "|" + d2 + "|" + d3 + "|" + dev;
            if (string.Equals(key, _ocrDlKey, StringComparison.Ordinal)) return d1;

            int rc = NativeDl.OcrDlCreate(_handle, d1, d2, d3, dev);
            if (rc != 0)
            {
                error = string.Equals(dev, "CUDA", StringComparison.OrdinalIgnoreCase)
                    ? "CUDA 推理尚未实现（接口已预留），请先用 CPU/GPU"
                    : VaResult.ToString(rc) + "：" + LastError;
                return null;
            }

            _ocrDlKey = key;
            return d1;
        }

        /// <summary>深度学习 OCR：返回每行文本；fullText 为整段（按阅读顺序空格连接）。</summary>
        public OpResult<List<NativeDl.OcrDlLine>> OcrDl(Bitmap src, string detModel, string recModel,
                                                        string dictPath, string device,
                                                        float boxThresh, float unclipRatio, int maxCount,
                                                        out string fullText)
        {
            fullText = "";
            if (src == null) return OpResult<List<NativeDl.OcrDlLine>>.Fail("输入图像为空");

            string err;
            string det = OcrDlEnsure(detModel, recModel, dictPath, device, out err);
            if (det == null) return OpResult<List<NativeDl.OcrDlLine>>.Fail(err);

            using (var ci = new ColorInput(src))
            {
                try
                {
                    VA_Image img = ci.Image;
                    List<NativeDl.OcrDlLine> lines =
                        NativeDl.OcrDlRun(_handle, ref img, boxThresh, unclipRatio, maxCount,
                                          out fullText, out err);
                    if (!string.IsNullOrEmpty(err))
                        return OpResult<List<NativeDl.OcrDlLine>>.Fail(err);
                    return OpResult<List<NativeDl.OcrDlLine>>.Ok(lines);
                }
                catch (Exception ex)
                {
                    return OpResult<List<NativeDl.OcrDlLine>>.Fail("推理异常：" + ex.Message);
                }
            }
        }

        // ---------------------- 相机标定（棋盘格） ----------------------

        /// <summary>在单张图上找棋盘格内角点，返回 [x0,y0,x1,y1,...]（图像坐标）。</summary>
        public bool CalibFindChessboard(Bitmap src, int cols, int rows,
                                        out double[] points, out string error)
        {
            points = null;
            error = "输入图像为空";
            if (src == null) return false;

            using (var ci = new ColorInput(src))
            {
                VA_Image img = ci.Image;
                points = NativeCalib.FindChessboard(_handle, ref img, cols, rows, out error);
                return points != null;
            }
        }

        /// <summary>用多张图的角点标定内参与畸变系数。</summary>
        public bool CalibCalibrate(double[] allPoints, int[] counts, int cols, int rows,
                                   int width, int height,
                                   out double[] k, out double[] dist, out double rms, out string error)
        {
            return NativeCalib.Calibrate(_handle, allPoints, counts, cols, rows, width, height,
                                         out k, out dist, out rms, out error);
        }

        /// <summary>模型解析摘要（输入尺寸 / 输出通道 / 类别数 / 关键点数）。</summary>
        public string DlDescribe(int task)
        {
            try { return NativeDl.Describe(_handle, task); }
            catch (Exception) { return ""; }
        }

        /// <summary>姿态关键点数（未加载时 0）。</summary>
        public int DlPoseKeypoints()
        {
            try { return NativeDl.PoseKeypoints(_handle); }
            catch (Exception) { return 0; }
        }

        /// <summary>深度估计：返回 0~1 归一化深度图（尺寸同输入）。</summary>
        public OpResult<float[]> DlDepth(Bitmap src, string modelPath, string device,
                                         out float min, out float max)
        {
            min = 0f; max = 0f;
            if (src == null) return OpResult<float[]>.Fail("输入图像为空");

            string err;
            string mp = DlEnsure(NativeDl.TaskDepth, modelPath, device, out err);
            if (mp == null) return OpResult<float[]>.Fail(err);

            using (var ci = new ColorInput(src))
            {
                try
                {
                    VA_Image img = ci.Image;
                    float[] map = NativeDl.DepthRun(_handle, ref img, src.Width, src.Height,
                                                    out min, out max, out err);
                    if (map == null) return OpResult<float[]>.Fail(err);
                    return OpResult<float[]>.Ok(map);
                }
                catch (Exception ex)
                {
                    return OpResult<float[]>.Fail("推理异常：" + ex.Message);
                }
            }
        }

        public OpResult<DetectOutput> Detect(Bitmap src, DetectParam param)
        {
            if (src == null) return OpResult<DetectOutput>.Fail("输入图像为空");
            if (param == null) param = DetectParam.Default();

            string mp = ResolveModelPath(param.ModelPath);
            string dev = string.IsNullOrEmpty(param.Device) ? "CPU" : param.Device;

            // ---- 模型加载 / 重载（按路径 + 设备缓存）----
            if (string.IsNullOrWhiteSpace(mp))
            {
                return OpResult<DetectOutput>.Fail("未指定模型路径");
            }
            if (!System.IO.File.Exists(mp))
            {
                return OpResult<DetectOutput>.Fail("模型文件不存在：" + mp);
            }
            if (!string.Equals(mp, _detModelPath, StringComparison.Ordinal)
                || !string.Equals(dev, _detDevice, StringComparison.Ordinal))
            {
                if (!string.IsNullOrEmpty(_detModelPath))
                {
                    try { NativeMethods.va_det_clear(_handle); }
                    catch (Exception) { /* 忽略清除失败，下面重建会覆盖 */ }
                }

                byte[] pPath = Utf8NullTerminated(mp);
                byte[] pDev = Utf8NullTerminated(dev);
                int rc = NativeMethods.va_det_create(_handle, pPath, pDev);
                if (rc != VaResult.OK)
                    return OpResult<DetectOutput>.Fail(VaResult.ToString(rc) + "：" + LastError);

                _detModelPath = mp;
                _detDevice = dev;
            }

            // ---- 推理（彩色输入，与颜色提取同款 ColorInput）----
            using (var ci = new ColorInput(src))
            {
                try
                {
                    VA_Image img = ci.Image;
                    int maxCount = param.MaxCount > 0 ? param.MaxCount : 100;
                    VA_DetectResult[] raw = new VA_DetectResult[maxCount];
                    int count = 0;

                    int rc = NativeMethods.va_det_run(_handle, ref img,
                        param.ScoreThresh, param.IouThresh, raw, maxCount, out count);
                    if (rc != VaResult.OK && rc != VaResult.ErrSize)
                        return OpResult<DetectOutput>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    string[] names = LoadClassNames();
                    int n = (rc == VaResult.ErrSize) ? raw.Length : Math.Min(count, raw.Length);

                    var outp = new DetectOutput { ClassNames = names, Detections = new List<DetectResult>(n) };
                    for (int i = 0; i < n; i++)
                        outp.Detections.Add(DetectResult.FromNative(raw[i], names));

                    return OpResult<DetectOutput>.Ok(outp);
                }
                catch (Exception ex)
                {
                    return OpResult<DetectOutput>.Fail("目标识别失败：" + ex.Message);
                }
            }
        }

        /// <summary>取当前模型的类别名表（索引 = class_id）。</summary>
        private string[] LoadClassNames()
        {
            int cnt = 0;
            NativeMethods.va_det_class_count(_handle, out cnt);
            if (cnt <= 0) return new string[0];

            var list = new string[cnt];
            byte[] buf = new byte[256];
            for (int i = 0; i < cnt; i++)
            {
                int n = NativeMethods.va_det_class_name(_handle, i, buf, buf.Length);
                list[i] = (n > 0) ? System.Text.Encoding.UTF8.GetString(buf, 0, n) : ("class_" + i);
            }
            return list;
        }

        // ---------------------- 字符识别（传统算法 OCR）----------------------

        /// <summary>设置 OCR 上下文默认参数（后续 Segment / Recognize 可逐次覆盖）。</summary>
        public OpResult SetOcrParam(OcrParam param)
        {
            if (param == null) param = OcrParam.Default();

            VA_OcrParam p = param.ToNative();
            int rc = NativeMethods.va_ocr_set_param(_handle, ref p);
            return rc == VaResult.OK
                ? OpResult.Ok("OCR 参数已设置")
                : OpResult.Fail(VaResult.ToString(rc) + "：" + LastError);
        }

        /// <summary>
        /// 添加一个字符模板：gray 为单个字符的灰度图，label 为该字符的标签（支持中文）。
        /// 上层约定「文件名即标签」，见 FlowRunner 的 OCRModel 目录加载。
        /// </summary>
        public OpResult AddOcrTemplate(Bitmap gray, string label)
        {
            if (gray == null) return OpResult.Fail("模板图像为空");

            using (var gi = new GrayInput(gray))
            {
                try
                {
                    VA_Image img = gi.Image;
                    int rc = NativeMethods.va_ocr_add_template(_handle, ref img,
                                                               Utf8NullTerminated(label ?? ""));
                    return rc == VaResult.OK
                        ? OpResult.Ok("已添加字符模板：" + (label ?? ""))
                        : OpResult.Fail(VaResult.ToString(rc) + "：" + LastError);
                }
                catch (Exception ex)
                {
                    return OpResult.Fail("添加字符模板失败：" + ex.Message);
                }
            }
        }

        /// <summary>取当前已加载的字符模板数量（0 表示没有模板，只能做字符分割）。</summary>
        public OpResult<int> OcrTemplateCount()
        {
            int count = 0;
            int rc = NativeMethods.va_ocr_template_count(_handle, out count);
            return rc == VaResult.OK
                ? OpResult<int>.Ok(count)
                : OpResult<int>.Fail(VaResult.ToString(rc) + "：" + LastError);
        }

        /// <summary>清空全部字符模板。</summary>
        public OpResult ClearOcrTemplates()
        {
            int rc = NativeMethods.va_ocr_clear_templates(_handle);
            return rc == VaResult.OK
                ? OpResult.Ok("字符模板已清空")
                : OpResult.Fail(VaResult.ToString(rc) + "：" + LastError);
        }

        /// <summary>
        /// 字符分割：只输出字符外接框（x/y/w/h 为左上角 + 宽高，图像坐标）。
        /// 原生不填 label / score（标签固定 "?"、置信度 0），需要文本请调 <see cref="Recognize"/>。
        /// </summary>
        public OpResult<List<OcrChar>> Segment(Bitmap gray, OcrParam param, int maxCount)
        {
            if (gray == null) return OpResult<List<OcrChar>>.Fail("输入图像为空");
            if (param == null) param = OcrParam.Default();
            if (maxCount <= 0) maxCount = 200;

            using (var gi = new GrayInput(gray))
            {
                try
                {
                    VA_Image img = gi.Image;
                    VA_OcrParam p = param.ToNative();

                    VA_OcrChar[] raw = NewOcrCharBuffer(maxCount);
                    int count = 0;

                    int rc = NativeMethods.va_ocr_segment(_handle, ref img, ref p,
                                                          raw, maxCount, out count);
                    if (rc != VaResult.OK && rc != VaResult.ErrSize)
                        return OpResult<List<OcrChar>>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    // ErrSize 表示原生缓冲区不够装，此时原生已填满整个数组
                    int n = (rc == VaResult.ErrSize) ? raw.Length : Math.Min(count, raw.Length);
                    var list = new List<OcrChar>(n);
                    for (int i = 0; i < n; i++)
                        list.Add(OcrChar.FromNative(raw[i]));

                    return OpResult<List<OcrChar>>.Ok(list);
                }
                catch (Exception ex)
                {
                    return OpResult<List<OcrChar>>.Fail("字符分割失败：" + ex.Message);
                }
            }
        }

        /// <summary>
        /// 字符识别：分割 + 模板匹配，输出整行文本（行间以 '\n' 分隔）与平均置信度。
        /// 模板为空时文本为空串（不算失败），仍可用 <see cref="Segment"/> 拿字符框。
        /// </summary>
        public OpResult<OcrResult> Recognize(Bitmap gray, OcrParam param)
        {
            if (gray == null) return OpResult<OcrResult>.Fail("输入图像为空");
            if (param == null) param = OcrParam.Default();

            using (var gi = new GrayInput(gray))
            {
                try
                {
                    VA_Image img = gi.Image;
                    VA_OcrParam p = param.ToNative();

                    byte[] textBuf = new byte[4096];
                    double meanScore = 0.0;

                    int rc = NativeMethods.va_ocr_recognize(_handle, ref img, ref p,
                                                            textBuf, textBuf.Length, out meanScore);
                    if (rc != VaResult.OK)
                        return OpResult<OcrResult>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    var res = new OcrResult
                    {
                        Text = Utf8FromBuffer(textBuf),
                        MeanScore = meanScore,
                        Chars = new List<OcrChar>()
                    };
                    return OpResult<OcrResult>.Ok(res);
                }
                catch (Exception ex)
                {
                    return OpResult<OcrResult>.Fail("字符识别失败：" + ex.Message);
                }
            }
        }

        /// <summary>分配 OCR 字符缓冲；含 ByValArray 的结构体必须逐个初始化数组字段才能封送。</summary>
        private static VA_OcrChar[] NewOcrCharBuffer(int n)
        {
            var arr = new VA_OcrChar[n];
            for (int i = 0; i < n; i++)
                arr[i].label = new byte[8];
            return arr;
        }

        /// <summary>
        /// 分割 + 识别**一次调用**：同时返回整段文本、平均置信度，以及每个字符的
        /// 外接框 + <b>真实标签与置信度</b>。
        ///
        /// 与 <see cref="Recognize"/> 的区别：这里 <see cref="OcrResult.Chars"/> 是识别结果
        /// （label / score 已填），不再是"分割出来的一堆问号"。上层若要画字符框，
        /// 必须用本方法，否则框上标签会与文本不一致（历史缺陷：框恒为 "? 0.00"）。
        /// </summary>
        /// <param name="gray">单通道（或可灰度化的）输入图，内部转灰度。</param>
        /// <param name="param">OCR 参数；null 用默认值。</param>
        /// <param name="maxCount">字符数组容量上限；&lt;=0 按 200。超出部分截断，不影响文本。</param>
        public OpResult<OcrResult> RecognizeEx(Bitmap gray, OcrParam param, int maxCount)
        {
            if (gray == null) return OpResult<OcrResult>.Fail("输入图像为空");
            if (param == null) param = OcrParam.Default();
            if (maxCount <= 0) maxCount = 200;

            using (var gi = new GrayInput(gray))
            {
                try
                {
                    VA_Image img = gi.Image;
                    VA_OcrParam p = param.ToNative();

                    byte[] textBuf = new byte[4096];
                    VA_OcrChar[] raw = NewOcrCharBuffer(maxCount);
                    int total = 0;
                    double meanScore = 0.0;

                    int rc = NativeMethods.va_ocr_recognize_ex(_handle, ref img, ref p,
                                                               raw, raw.Length, out total,
                                                               textBuf, textBuf.Length,
                                                               out meanScore);
                    if (rc != VaResult.OK)
                        return OpResult<OcrResult>.Fail(VaResult.ToString(rc) + "：" + LastError);

                    // 原生把「截断前的真实总数」写进 total，这里按实际写入的容量取用
                    int n = (total < raw.Length) ? total : raw.Length;
                    if (n < 0) n = 0;

                    var chars = new List<OcrChar>(n);
                    for (int i = 0; i < n; i++)
                        chars.Add(OcrChar.FromNative(raw[i]));

                    var res = new OcrResult
                    {
                        Text = Utf8FromBuffer(textBuf),
                        MeanScore = meanScore,
                        Chars = chars
                    };
                    return OpResult<OcrResult>.Ok(res);
                }
                catch (Exception ex)
                {
                    return OpResult<OcrResult>.Fail("字符识别失败：" + ex.Message);
                }
            }
        }

        /// <summary>把字符串转 UTF-8 字节并补 null 结尾（供原生 const char* 参数，支持中文路径）。</summary>
        private static byte[] Utf8NullTerminated(string s)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s ?? "");
            byte[] result = new byte[bytes.Length + 1];
            Array.Copy(bytes, result, bytes.Length);
            result[bytes.Length] = 0;
            return result;
        }

        /// <summary>把原生写回的 UTF-8 字节缓冲解成字符串（截断到第一个 '\0'）。</summary>
        private static string Utf8FromBuffer(byte[] buf)
        {
            if (buf == null || buf.Length == 0) return string.Empty;

            int n = Array.IndexOf(buf, (byte)0);
            if (n < 0) n = buf.Length;
            return System.Text.Encoding.UTF8.GetString(buf, 0, n);
        }

        /// <summary>
        /// 解析模型路径：原样存在则直接用；否则尝试相对「程序基目录」或「基目录/models」下按文件名查找。
        /// 都找不到时返回原值（交给上层报出「文件不存在」的可操作提示）。
        /// </summary>
        private static string ResolveModelPath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return p ?? "";
            if (System.IO.File.Exists(p)) return p;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
            string cand = System.IO.Path.Combine(baseDir, p);
            if (System.IO.File.Exists(cand)) return cand;

            string fn = System.IO.Path.GetFileName(p);
            string cand2 = System.IO.Path.Combine(baseDir, "models", fn);
            if (System.IO.File.Exists(cand2)) return cand2;

            return p;
        }

        /// <summary>把原生卡尺点数组转成托管列表（未命中的也保留，供可视化区分）。</summary>
        private static List<CaliperPoint> ToCaliperPoints(VA_CaliperPoint[] raw, int count)
        {
            var list = new List<CaliperPoint>();
            if (raw == null) return list;

            int n = count;
            if (n < 0) n = 0;
            if (n > raw.Length) n = raw.Length;

            for (int i = 0; i < n; i++)
                list.Add(CaliperPoint.FromNative(raw[i]));

            return list;
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

        /// <summary>该像素格式是否是「能被算法库当彩色处理」的格式（24bpp / 32bpp）。</summary>
        internal static bool IsColorFormat(PixelFormat format)
        {
            int ch = ChannelsOf(format);
            return ch == 3 || ch == 4;
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

        /// <summary>
        /// 彩色算法输入：把任意位图统一转成 <strong>24bpp BGR 连续缓冲区</strong>再交给算法库
        /// （channels=3）。与 <see cref="GrayInput"/> 互为补充 ——
        /// 颜色提取与彩色滤波必须走这里，否则拿到的是 1 通道灰度，颜色信息已经丢失。
        /// <para>
        /// GDI+ 的 24bpp 内存字节序就是 BGR，与 OpenCV 的 BGR 一致，因此这里不做通道交换。
        /// 数据固定在托管数组中（pinned），调用期间不会被 GC 移动，Dispose 时释放。
        /// </para>
        /// </summary>
        private sealed class ColorInput : IDisposable
        {
            public VA_Image Image;

            private GCHandle _pin;
            private Bitmap _owned;

            public ColorInput(Bitmap src)
            {
                if (src == null) throw new ArgumentNullException("src");

                int w = src.Width;
                int h = src.Height;
                byte[] bgr = new byte[w * h * 3];

                Bitmap work = src;
                if (work.PixelFormat != PixelFormat.Format24bppRgb)
                {
                    // 32bpp / 索引 / 16bpp 等先经 GDI+ 归一化到 24bpp（丢 Alpha，保留颜色）
                    work = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(work))
                        g.DrawImage(src, 0, 0, w, h);
                    _owned = work;
                }

                var rect = new Rectangle(0, 0, w, h);
                BitmapData d = work.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = d.Stride;
                    int rowBytes = w * 3;
                    byte[] row = new byte[stride];

                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(IntPtr.Add(d.Scan0, y * stride), row, 0, stride);
                        // 去掉行尾填充，排成连续缓冲（算法库按 stride = w*3 读）
                        Buffer.BlockCopy(row, 0, bgr, y * rowBytes, rowBytes);
                    }
                }
                finally
                {
                    work.UnlockBits(d);
                }

                _pin = GCHandle.Alloc(bgr, GCHandleType.Pinned);

                Image = new VA_Image();
                Image.data = _pin.AddrOfPinnedObject();
                Image.width = w;
                Image.height = h;
                Image.channels = 3;     // 交给算法库的是 3 通道 BGR
                Image.stride = w * 3;
            }

            public void Dispose()
            {
                if (_pin.IsAllocated) _pin.Free();
                if (_owned != null) { _owned.Dispose(); _owned = null; }
            }
        }

        /// <summary>创建 24bpp 彩色位图并返回已锁定的 BitmapData（供原生直接写入）。</summary>
        internal static Bitmap CreateColorBitmap(int width, int height, out BitmapData data)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            data = bmp.LockBits(new Rectangle(0, 0, width, height),
                                ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            return bmp;
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

    /// <summary>
    /// 滤波类型（与 C 端 VA_FILTER_* 一一对应，<strong>编号不同不要混用</strong>）。
    /// </summary>
    public static class FilterType
    {
        /// <summary>原样输出（只做可选形态学）。</summary>
        public const int None = 0;
        /// <summary>均值滤波。</summary>
        public const int Box = 1;
        /// <summary>高斯滤波（默认）。</summary>
        public const int Gauss = 2;
        /// <summary>中值滤波（去椒盐噪声）。</summary>
        public const int Median = 3;
        /// <summary>双边滤波（保边去噪）。</summary>
        public const int Bilateral = 4;
        /// <summary>锐化：USM（高斯模糊后反相叠加）。</summary>
        public const int Usm = 5;
        /// <summary>自定义 3x3 卷积核（原生内为固定锐化核）。</summary>
        public const int Custom = 6;

        /// <summary>类型值 → 中文名（未知值返回原文）。</summary>
        public static string Name(int type)
        {
            switch (type)
            {
                case None:      return "原样";
                case Box:       return "均值";
                case Gauss:     return "高斯";
                case Median:    return "中值";
                case Bilateral: return "双边";
                case Usm:       return "锐化";
                case Custom:    return "自定义";
                default:        return "类型" + type;
            }
        }
    }

    /// <summary>
    /// 滤波后形态学算子（与 C 端 VA_MORPH_* 一一对应）。
    /// ⚠️ 与 <see cref="PreprocessParam.MorphOp"/>（0 无 / 1 开 / 2 闭）<strong>编号不同</strong>，
    /// 这里是 OpenCV MorphTypes 的编号，两者不要互相套用。
    /// </summary>
    public static class FilterMorphOp
    {
        /// <summary>不做形态学。</summary>
        public const int None = 0;
        public const int Dilate = 1;
        public const int Erode = 2;
        public const int Open = 3;
        public const int Close = 4;
        public const int Gradient = 5;
        public const int TopHat = 6;
        public const int BlackHat = 7;

        /// <summary>算子值 → 中文名（未知值返回原文）。</summary>
        public static string Name(int op)
        {
            switch (op)
            {
                case None:     return "无";
                case Dilate:   return "膨胀";
                case Erode:    return "腐蚀";
                case Open:     return "开运算";
                case Close:    return "闭运算";
                case Gradient: return "梯度";
                case TopHat:   return "顶帽";
                case BlackHat: return "黑帽";
                default:       return "运算" + op;
            }
        }
    }

    /// <summary>形态学结构化元素形状（与 C 端 VA_MORPH_SHAPE_* 一一对应）。</summary>
    public static class FilterMorphShape
    {
        public const int Rect = 0;
        public const int Ellipse = 1;
        public const int Cross = 2;
    }

    /// <summary>
    /// 图像滤波参数（对应 <see cref="VA_FilterParam"/>）。
    /// <see cref="KeepColor"/> 只影响托管侧走哪条链路，不传给原生。
    /// </summary>
    public class FilterParam
    {
        /// <summary>滤波类型（见 <see cref="FilterType"/>）。</summary>
        public int Type { get; set; }

        /// <summary>核边长（奇数；<![CDATA[<3]]> 时用算子默认值）。</summary>
        public int Ksize { get; set; }

        /// <summary>高斯 / 双边的 sigma；&lt;=0 时由 ksize 推导。</summary>
        public double Sigma { get; set; }

        /// <summary>锐化强度（USM），典型 1.0；0 = 不锐化。</summary>
        public double Amount { get; set; }

        /// <summary>形态学算子（见 <see cref="FilterMorphOp"/>）。</summary>
        public int MorphOp { get; set; }

        /// <summary>形态学结构化元素形状（见 <see cref="FilterMorphShape"/>）。</summary>
        public int MorphShape { get; set; }

        /// <summary>形态学核边长；&lt;3 则不做形态学。</summary>
        public int MorphKsize { get; set; }

        /// <summary>形态学迭代次数；&lt;1 按 1 处理。</summary>
        public int Iterations { get; set; }

        /// <summary>
        /// 是否按<strong>彩色</strong>处理：true = 3 通道进 3 通道出（输入必须是 24bpp / 32bpp）；
        /// false = 转灰度后 1 通道进 1 通道出。仅托管侧生效。
        /// </summary>
        public bool KeepColor { get; set; }

        public static FilterParam Default()
        {
            return new FilterParam
            {
                Type = FilterType.Gauss,
                Ksize = 5,
                Sigma = 0,
                Amount = 1.0,
                MorphOp = FilterMorphOp.None,
                MorphShape = FilterMorphShape.Rect,
                MorphKsize = 3,
                Iterations = 1,
                KeepColor = false
            };
        }

        internal VA_FilterParam ToNative()
        {
            VA_FilterParam p = new VA_FilterParam();
            p.type = Type;
            p.ksize = Ksize;
            p.sigma = Sigma;
            p.amount = Amount;
            p.morph_op = MorphOp;
            p.morph_shape = MorphShape;
            p.morph_ksize = MorphKsize;
            p.iterations = Iterations;
            return p;
        }
    }

    /// <summary>颜色空间（与 C 端 VA_COLOR_SPACE_* 一一对应）。</summary>
    public static class ColorSpace
    {
        /// <summary>BGR 三通道独立阈值（0~255）。</summary>
        public const int Bgr = 0;

        /// <summary>HSV 阈值。⚠ H 取值 0~179（OpenCV 口径，不是 0~360），S / V 取 0~255。</summary>
        public const int Hsv = 1;
    }

    /// <summary>颜色分割后的形态学清理算子（与 C 端 VA_COLOR_MORPH_* 一一对应）。</summary>
    public static class ColorMorphOp
    {
        public const int None = 0;
        public const int Open = 1;
        public const int Close = 2;
    }

    /// <summary>
    /// 颜色提取参数（对应 <see cref="VA_ColorParam"/>）。
    /// <see cref="OutputMask"/>、MinArea / MaxArea / MaxCount 里的后三项同时用于托管侧，
    /// MinArea / MaxArea / MaxCount 也会原样传给原生。
    /// </summary>
    public class ColorExtractParam
    {
        /// <summary>颜色空间（见 <see cref="ColorSpace"/>）。</summary>
        public int Space { get; set; }

        /// <summary>下界三分量（B/H, G/S, R/V）。</summary>
        public int[] Low { get; set; }

        /// <summary>上界三分量（B/H, G/S, R/V）。</summary>
        public int[] High { get; set; }

        /// <summary>形态学清理算子（见 <see cref="ColorMorphOp"/>）。</summary>
        public int MorphOp { get; set; }

        /// <summary>形态学核边长；&lt;3 则不做。</summary>
        public int MorphKsize { get; set; }

        /// <summary>连通域面积下限（像素）。</summary>
        public double MinArea { get; set; }

        /// <summary>连通域面积上限（像素）；&lt;=0 表示不设上限。</summary>
        public double MaxArea { get; set; }

        /// <summary>最多返回的目标数。</summary>
        public int MaxCount { get; set; }

        /// <summary>是否让原生产出 0/255 掩膜图；false 时结果里 Mask 为 null。</summary>
        public bool OutputMask { get; set; }

        public static ColorExtractParam Default()
        {
            return new ColorExtractParam
            {
                Space = ColorSpace.Hsv,
                Low = new[] { 0, 0, 0 },
                High = new[] { 179, 255, 255 },
                MorphOp = ColorMorphOp.None,
                MorphKsize = 3,
                MinArea = 10,
                MaxArea = 0,
                MaxCount = 50,
                OutputMask = true
            };
        }

        internal VA_ColorParam ToNative()
        {
            VA_ColorParam p = new VA_ColorParam();
            p.space = Space;
            p.low = new int[4];
            p.high = new int[4];

            for (int i = 0; i < 3; i++)
            {
                p.low[i] = (Low != null && i < Low.Length) ? Low[i] : 0;
                p.high[i] = (High != null && i < High.Length) ? High[i] : 0;
            }

            p.morph_op = MorphOp;
            p.morph_ksize = MorphKsize;
            return p;
        }
    }

    /// <summary>
    /// 颜色提取结果：命中区域列表 + 命中统计 + 可选掩膜。
    /// <see cref="Mask"/> 的所有权随结果移交调用方，不用时必须 Dispose。
    /// </summary>
    public class ColorExtractResult
    {
        /// <summary>命中区域（面积降序，最多 MaxCount 个）。</summary>
        public List<BlobResult> Blobs { get; private set; }

        /// <summary>命中像素数。</summary>
        public double PixelCount { get; set; }

        /// <summary>命中像素 / 总像素，0~1。</summary>
        public double Ratio { get; set; }

        /// <summary>命中区域内的平均颜色（BGR 字节序）。</summary>
        public double MeanB { get; set; }
        /// <summary>命中区域内的平均颜色（G）。</summary>
        public double MeanG { get; set; }
        /// <summary>命中区域内的平均颜色（R）。</summary>
        public double MeanR { get; set; }

        /// <summary>0/255 掩膜（8bpp 灰度）；未请求时为 null。调用方负责释放。</summary>
        public Bitmap Mask { get; set; }

        public ColorExtractResult()
        {
            Blobs = new List<BlobResult>();
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

    /// <summary>卡尺边缘极性：沿投影正方向（直线法线 / 圆径向外）观察灰度变化。</summary>
    public static class CaliperPolarity
    {
        /// <summary>不过滤极性（默认）。</summary>
        public const int Any = 0;
        /// <summary>由暗变亮。</summary>
        public const int DarkToLight = 1;
        /// <summary>由亮变暗。</summary>
        public const int LightToDark = 2;
    }

    /// <summary>单个卡尺命中多个候选边缘时的选边策略。</summary>
    public static class CaliperSelectMode
    {
        /// <summary>取跳变幅值最大的一个（默认）。</summary>
        public const int Strongest = 0;
        /// <summary>取沿投影方向最靠前的一个。</summary>
        public const int First = 1;
        /// <summary>取沿投影方向最靠后的一个。</summary>
        public const int Last = 2;
    }

    /// <summary>
    /// 卡尺参数。字段与 <see cref="VA_CaliperParam"/> 同名同义；
    /// 直线模式用 X1/Y1/X2/Y2，圆模式用 Cx/Cy/InnerRadius/OuterRadius/StartAngle/AngleSpan。
    /// 角度单位为度，0° = +X 轴，顺时针为正。
    /// </summary>
    public class CaliperParam
    {
        /// <summary>扫描线段起点 X（图像坐标）。</summary>
        public double X1 { get; set; }
        /// <summary>扫描线段起点 Y（图像坐标）。</summary>
        public double Y1 { get; set; }
        /// <summary>扫描线段终点 X（图像坐标）。</summary>
        public double X2 { get; set; }
        /// <summary>扫描线段终点 Y（图像坐标）。</summary>
        public double Y2 { get; set; }

        /// <summary>圆心 X（图像坐标）。</summary>
        public double Cx { get; set; }
        /// <summary>圆心 Y（图像坐标）。</summary>
        public double Cy { get; set; }
        /// <summary>内径。</summary>
        public double InnerRadius { get; set; }
        /// <summary>外径；小于或等于内径时原生侧按 内径 + ProjectionLength 处理。</summary>
        public double OuterRadius { get; set; }
        /// <summary>起始角（度）。</summary>
        public double StartAngle { get; set; }
        /// <summary>角度跨度（度）；大于 360 时原生侧截断为 360。</summary>
        public double AngleSpan { get; set; }

        /// <summary>卡尺个数（直线默认 10 / 圆默认 20）。</summary>
        public int CaliperCount { get; set; }
        /// <summary>投影窗口长度（px），默认 20。</summary>
        public double ProjectionLength { get; set; }
        /// <summary>边缘极性，见 <see cref="CaliperPolarity"/>，默认 Any。</summary>
        public int Polarity { get; set; }
        /// <summary>边缘跳变幅值阈值（灰度差），默认 30。</summary>
        public double EdgeThreshold { get; set; }
        /// <summary>一维均值滤波半宽，默认 2。</summary>
        public int FilterHalfWidth { get; set; }
        /// <summary>选边策略，见 <see cref="CaliperSelectMode"/>，默认 Strongest。</summary>
        public int SelectMode { get; set; }
        /// <summary>有效边缘点下限（直线 3 / 圆 5）。</summary>
        public int MinEdgeCount { get; set; }
        /// <summary>是否剔除离群点一轮（默认 1）。</summary>
        public int RejectOutliers { get; set; }
        /// <summary>残差剔除阈值（px），默认 2.0。</summary>
        public double OutlierTolerance { get; set; }

        /// <summary>直线卡尺默认参数（对齐 PRD 假设 C）。</summary>
        public static CaliperParam LineDefault()
        {
            return new CaliperParam
            {
                X1 = 0.0, Y1 = 0.0, X2 = 100.0, Y2 = 0.0,
                Cx = 0.0, Cy = 0.0, InnerRadius = 0.0, OuterRadius = 0.0,
                StartAngle = 0.0, AngleSpan = 360.0,
                CaliperCount = 10,
                ProjectionLength = 20.0,
                Polarity = CaliperPolarity.Any,
                EdgeThreshold = 30.0,
                FilterHalfWidth = 2,
                SelectMode = CaliperSelectMode.Strongest,
                MinEdgeCount = 3,
                RejectOutliers = 1,
                OutlierTolerance = 2.0
            };
        }

        /// <summary>圆卡尺默认参数（对齐 PRD 假设 C）。</summary>
        public static CaliperParam CircleDefault()
        {
            var p = LineDefault();
            p.X1 = 0.0; p.Y1 = 0.0; p.X2 = 0.0; p.Y2 = 0.0;
            p.InnerRadius = 80.0;
            p.OuterRadius = 120.0;
            p.StartAngle = 0.0;
            p.AngleSpan = 360.0;
            p.CaliperCount = 20;
            p.MinEdgeCount = 5;
            return p;
        }

        internal VA_CaliperParam ToNative()
        {
            VA_CaliperParam p = new VA_CaliperParam();
            p.x1 = X1; p.y1 = Y1; p.x2 = X2; p.y2 = Y2;
            p.cx = Cx; p.cy = Cy;
            p.inner_radius = InnerRadius;
            p.outer_radius = OuterRadius;
            p.start_angle = StartAngle;
            p.angle_span = AngleSpan;
            p.caliper_count = CaliperCount;
            p.projection_length = ProjectionLength;
            p.polarity = Polarity;
            p.edge_threshold = EdgeThreshold;
            p.filter_half_width = FilterHalfWidth;
            p.select_mode = SelectMode;
            p.min_edge_count = MinEdgeCount;
            p.reject_outliers = RejectOutliers;
            p.outlier_tolerance = OutlierTolerance;
            return p;
        }

        public override string ToString()
        {
            return string.Format("卡尺 {0} 个，投影 {1:F0} px，阈值 {2:F0}，最少 {3} 点",
                CaliperCount, ProjectionLength, EdgeThreshold, MinEdgeCount);
        }
    }

    /// <summary>单个卡尺的输出：投影窗口两端（未命中也有效）+ 命中边缘点。</summary>
    public class CaliperPoint
    {
        /// <summary>投影窗口起点 X（图像坐标）。</summary>
        public double Px1 { get; set; }
        /// <summary>投影窗口起点 Y（图像坐标）。</summary>
        public double Py1 { get; set; }
        /// <summary>投影窗口终点 X（图像坐标）。</summary>
        public double Px2 { get; set; }
        /// <summary>投影窗口终点 Y（图像坐标）。</summary>
        public double Py2 { get; set; }
        /// <summary>命中边缘点 X（亚像素，图像坐标）。</summary>
        public double X { get; set; }
        /// <summary>命中边缘点 Y（亚像素，图像坐标）。</summary>
        public double Y { get; set; }
        /// <summary>边缘强度（灰度跳变幅值）。</summary>
        public double Strength { get; set; }
        /// <summary>是否命中。</summary>
        public bool Valid { get; set; }

        public CaliperPoint() { }

        internal static CaliperPoint FromNative(VA_CaliperPoint p)
        {
            return new CaliperPoint
            {
                Px1 = p.px1, Py1 = p.py1, Px2 = p.px2, Py2 = p.py2,
                X = p.x, Y = p.y,
                Strength = p.strength,
                Valid = p.valid != 0
            };
        }

        public override string ToString()
        {
            return Valid
                ? string.Format("({0:F2}, {1:F2}) 强度 {2:F1}", X, Y, Strength)
                : "未命中";
        }
    }

    /// <summary>直线卡尺结果：命中点 + 拟合线段 + 直线度残差。</summary>
    public class CaliperLineResult
    {
        /// <summary>全部卡尺（含未命中，供可视化区分）。</summary>
        public List<CaliperPoint> Points { get; set; }
        /// <summary>拟合线段（图像坐标）。</summary>
        public LineResult Line { get; set; }
        /// <summary>直线度残差 RMS（px）。</summary>
        public double Rms { get; set; }

        public CaliperLineResult()
        {
            Points = new List<CaliperPoint>();
            Line = new LineResult();
        }

        /// <summary>拟合线段长度（px）。</summary>
        public double Length
        {
            get { return Line == null ? 0.0 : Line.Length; }
        }

        /// <summary>拟合直线角度（度），归一化到 (-90°, 90°]。</summary>
        public double AngleDeg
        {
            get
            {
                if (Line == null) return 0.0;
                double a = Math.Atan2(Line.Y2 - Line.Y1, Line.X2 - Line.X1) * 180.0 / Math.PI;
                while (a <= -90.0) a += 180.0;
                while (a > 90.0) a -= 180.0;
                return a;
            }
        }

        /// <summary>命中的边缘点个数。</summary>
        public int ValidCount
        {
            get
            {
                int n = 0;
                if (Points == null) return 0;
                for (int i = 0; i < Points.Count; i++)
                    if (Points[i].Valid) n++;
                return n;
            }
        }

        public override string ToString()
        {
            return string.Format("长度 {0:F2} px / 角度 {1:F2}° / RMS {2:F2} / 有效点 {3}/{4}",
                Length, AngleDeg, Rms, ValidCount, Points == null ? 0 : Points.Count);
        }
    }

    /// <summary>圆卡尺结果：命中点 + 圆心半径 + 圆度残差。</summary>
    public class CaliperCircleResult
    {
        /// <summary>全部卡尺（含未命中，供可视化区分）。</summary>
        public List<CaliperPoint> Points { get; set; }
        /// <summary>拟合圆（residual 字段回填圆度 RMS）。</summary>
        public CircleResult Circle { get; set; }
        /// <summary>圆度残差 RMS（px）。</summary>
        public double Rms { get; set; }

        public CaliperCircleResult()
        {
            Points = new List<CaliperPoint>();
            Circle = new CircleResult();
        }

        /// <summary>拟合直径（px）。</summary>
        public double Diameter
        {
            get { return Circle == null ? 0.0 : Circle.Radius * 2.0; }
        }

        /// <summary>命中的边缘点个数。</summary>
        public int ValidCount
        {
            get
            {
                int n = 0;
                if (Points == null) return 0;
                for (int i = 0; i < Points.Count; i++)
                    if (Points[i].Valid) n++;
                return n;
            }
        }

        public override string ToString()
        {
            return string.Format("直径 {0:F2} px / 圆心 ({1:F2}, {2:F2}) / RMS {3:F2} / 有效点 {4}/{5}",
                Diameter, Circle == null ? 0.0 : Circle.Cx, Circle == null ? 0.0 : Circle.Cy,
                Rms, ValidCount, Points == null ? 0 : Points.Count);
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

    /// <summary>目标识别（OpenVINO YOLO）参数（对应原生 va_det_*）。</summary>
    public class DetectParam
    {
        /// <summary>模型路径（.onnx；Ultralytics 导出，输出 [1,4+nc,anchors]）。</summary>
        public string ModelPath { get; set; }

        /// <summary>推理设备名（"CPU" / "GPU" 等）；空串按 "CPU" 处理。</summary>
        public string Device { get; set; }

        /// <summary>类别置信度阈值（0~1），低于该值的框被丢弃。</summary>
        public float ScoreThresh { get; set; }

        /// <summary>NMS IoU 阈值（0~1），同类重叠高于该值则合并。</summary>
        public float IouThresh { get; set; }

        /// <summary>最多保留的目标数（<=0 视为 100）。</summary>
        public int MaxCount { get; set; }

        public static DetectParam Default()
        {
            return new DetectParam
            {
                ModelPath = "models/yolo11n.onnx",
                Device = "CPU",
                ScoreThresh = 0.25f,
                IouThresh = 0.45f,
                MaxCount = 100
            };
        }
    }

    /// <summary>单个检测结果：原图像素坐标系下的边界框（左上角 + 宽高）+ 类别 + 置信度。</summary>
    public class DetectResult
    {
        /// <summary>左上角 X（原图像素坐标）。</summary>
        public double X { get; set; }

        /// <summary>左上角 Y（原图像素坐标）。</summary>
        public double Y { get; set; }

        /// <summary>宽（图像像素）。</summary>
        public double Width { get; set; }

        /// <summary>高（图像像素）。</summary>
        public double Height { get; set; }

        /// <summary>类别索引（与 <see cref="DetectOutput.ClassNames"/> 下标对应）。</summary>
        public int ClassId { get; set; }

        /// <summary>置信度 0~1。</summary>
        public double Score { get; set; }

        /// <summary>类别名（从 <see cref="DetectOutput.ClassNames"/> 取，缺失时为 "class_N"）。</summary>
        public string ClassName { get; set; }

        internal static DetectResult FromNative(VA_DetectResult r, string[] names)
        {
            string name = (names != null && r.class_id >= 0 && r.class_id < names.Length)
                ? names[r.class_id]
                : ("class_" + r.class_id);
            return new DetectResult
            {
                X = r.x, Y = r.y, Width = r.w, Height = r.h,
                ClassId = r.class_id, Score = r.score, ClassName = name
            };
        }

        public override string ToString()
        {
            return string.Format("[{0}] ({1:F1},{2:F1}) {3:F0}x{4:F0} score={5:F3}",
                ClassName, X, Y, Width, Height, Score);
        }
    }

    /// <summary>
    /// 字符识别（传统算法 OCR）参数。哨兵值与原生一致：
    /// Threshold&lt;0 → Otsu；MinCharH / MaxCharH / MinAspect / MaxAspect &lt;=0 → 不限制；
    /// MaxGap&lt;0 → 3；NormW / NormH &lt;=0 → 16 / 24。
    /// </summary>
    public class OcrParam
    {
        /// <summary>二值化阈值 0~255；负数走 Otsu 自适应（默认 -1）。</summary>
        public int Threshold { get; set; }

        /// <summary>
        /// 极性控制：false = <b>自动判定极性</b>（推荐，白底黑字 / 黑底白字都能正确分割）；
        /// true = 强制反色（仅极端成像下需要人工干预时使用）。
        /// </summary>
        public bool Invert { get; set; }

        /// <summary>字符最小高度（像素）；&lt;=0 不限制。</summary>
        public double MinCharH { get; set; }

        /// <summary>字符最大高度（像素）；&lt;=0 不限制。</summary>
        public double MaxCharH { get; set; }

        /// <summary>最小宽高比 w/h；&lt;=0 不限制。</summary>
        public double MinAspect { get; set; }

        /// <summary>最大宽高比 w/h；&lt;=0 不限制。</summary>
        public double MaxAspect { get; set; }

        /// <summary>同行相邻字符最大间隙（像素）；&lt;0 按 3。</summary>
        public int MaxGap { get; set; }

        /// <summary>归一化宽；&lt;=0 按 16。</summary>
        public int NormW { get; set; }

        /// <summary>归一化高；&lt;=0 按 24。</summary>
        public int NormH { get; set; }

        /// <summary>匹配接受阈值 0~1；低于该值输出 '?'（默认 0.6）。</summary>
        public double AcceptScore { get; set; }

        public static OcrParam Default()
        {
            return new OcrParam
            {
                Threshold = -1,
                Invert = false,
                MinCharH = 0.0,
                MaxCharH = 0.0,
                MinAspect = 0.0,
                MaxAspect = 0.0,
                MaxGap = 3,
                NormW = 16,
                NormH = 24,
                AcceptScore = 0.6
            };
        }

        internal VA_OcrParam ToNative()
        {
            VA_OcrParam p = new VA_OcrParam();
            p.threshold = Threshold;
            p.invert = Invert ? 1 : 0;
            p.min_char_h = MinCharH;
            p.max_char_h = MaxCharH;
            p.min_aspect = MinAspect;
            p.max_aspect = MaxAspect;
            p.max_gap = MaxGap;
            p.norm_w = NormW;
            p.norm_h = NormH;
            p.accept_score = AcceptScore;
            return p;
        }

        public override string ToString()
        {
            return string.Format("阈值{0} 高[{1:F0}~{2:F0}] 宽高比[{3:F2}~{4:F2}] 归一{5}x{6} 接受{7:F2}",
                Threshold < 0 ? "Otsu" : Threshold.ToString(),
                MinCharH, MaxCharH, MinAspect, MaxAspect, NormW, NormH, AcceptScore);
        }
    }

    /// <summary>单个字符：左上角 + 宽高（图像像素坐标）+ 标签 + 置信度。</summary>
    public class OcrChar
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }

        /// <summary>识别出的字符（UTF-8 解码，支持中文）；纯分割时为 "?"。</summary>
        public string Label { get; set; }

        /// <summary>匹配置信度 0~1；纯分割时为 0。</summary>
        public double Score { get; set; }

        public OcrChar() { Label = ""; }

        internal static OcrChar FromNative(VA_OcrChar c)
        {
            return new OcrChar
            {
                X = c.x, Y = c.y, W = c.w, H = c.h,
                Label = Utf8Label(c.label),
                Score = c.score
            };
        }

        /// <summary>解码定长 8 字节 UTF-8 标签（截断到第一个 '\0'）。</summary>
        internal static string Utf8Label(byte[] label)
        {
            if (label == null || label.Length == 0) return string.Empty;

            int n = Array.IndexOf(label, (byte)0);
            if (n < 0) n = label.Length;
            return System.Text.Encoding.UTF8.GetString(label, 0, n);
        }

        public override string ToString()
        {
            return string.Format("{0} ({1:F1},{2:F1}) {3:F0}x{4:F0} score={5:F3}",
                Label, X, Y, W, H, Score);
        }
    }

    /// <summary>一次字符识别结果：整行文本（行间 '\n'）+ 平均置信度 + 可选字符框。</summary>
    public class OcrResult
    {
        /// <summary>识别文本；多行为 '\n' 分隔。模板为空时为空串。</summary>
        public string Text { get; set; }

        /// <summary>全体字符的平均置信度 0~1。</summary>
        public double MeanScore { get; set; }

        /// <summary>
        /// 字符框列表。<see cref="VisionAlgo.RecognizeEx"/> 返回带真实 label / score 的识别字符；
        /// <see cref="VisionAlgo.Segment"/> 返回只有框、标签恒为 "?" 的分割结果；
        /// <see cref="VisionAlgo.Recognize"/> 返回空列表（它只出文本）。
        /// </summary>
        public List<OcrChar> Chars { get; set; }

        public OcrResult()
        {
            Text = string.Empty;
            MeanScore = 0.0;
            Chars = new List<OcrChar>();
        }

        /// <summary>是否识别出了非空文本。</summary>
        public bool HasText { get { return !string.IsNullOrEmpty(Text); } }
    }

    /// <summary>目标识别（OpenVINO YOLO）的一次推理结果：类别名表 + 检测框列表。</summary>
    public class DetectOutput
    {
        /// <summary>类别名表，下标 = class_id。</summary>
        public string[] ClassNames { get; set; }

        /// <summary>检测到的目标列表（按原生输出顺序，已按 NMS 去重）。</summary>
        public List<DetectResult> Detections { get; set; }

        public DetectOutput()
        {
            ClassNames = new string[0];
            Detections = new List<DetectResult>();
        }

        /// <summary>置信度最高的目标（用于画布高亮 + 摘要首条）；无检测时返回 null。</summary>
        public DetectResult Best
        {
            get
            {
                DetectResult best = null;
                if (Detections != null)
                {
                    double s = -1.0;
                    foreach (DetectResult d in Detections)
                    {
                        if (d != null && d.Score > s) { s = d.Score; best = d; }
                    }
                }
                return best;
            }
        }
    }

    #endregion
}
