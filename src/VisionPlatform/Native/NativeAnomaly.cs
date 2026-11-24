using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace VisionPlatform.Native
{
    /// <summary>
    /// 无监督缺陷检测（PaDiM / EfficientAD）的原生入口。
    /// <para>
    /// 与 <see cref="NativeDl"/> 是并列的两套：那边是"有监督"的检测/分割/姿态，
    /// 这边是"只给 OK 样本"的异常检测，输出热图与缺陷统计。
    /// </para>
    /// <para>
    /// 字符串（模型目录 / 设备名）按 UTF-8 字节传，避免默认 ANSI 封送破坏中文路径。
    /// </para>
    /// </summary>
    public static class NativeAnomaly
    {
        private const string Dll = "VisionAlgo.Native.dll";

        /// <summary>模型类型（与 C 端 VA_ANOM_KIND_* 一致）。</summary>
        public const int KindPaDiM = 0;
        public const int KindEfficientAD = 1;

        /// <summary>特征提取后端（PaDiM 专用）。</summary>
        public const int FeatHandcraft = 0;
        public const int FeatOpenVino = 1;

        /// <summary>单帧缺陷统计。字段顺序必须与 C 端 <c>VA_AnomStat</c> 严格一致。</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct VA_AnomStat
        {
            public float max_score;     // 最大异常分（相对阈值倍数，1.0 = 阈值）
            public float mean_score;    // 平均异常分（相对阈值倍数）
            public float raw_max;       // 原始最大分
            public float threshold;     // 实际使用的阈值（原始刻度）

            public float area_ratio;    // 超阈值面积占比 0~1
            public int area_px;         // 超阈值像素数（只算够大的连通域）
            public int defect_count;    // 缺陷个数

            public int max_x;           // 最大分点位（原图坐标）
            public int max_y;

            public float box_x;         // 最大缺陷外接框（原图坐标）
            public float box_y;
            public float box_w;
            public float box_h;

            public int roi_x;           // 实际分析区域（原图坐标）
            public int roi_y;
            public int roi_w;
            public int roi_h;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_train_begin(IntPtr handle, byte[] out_dir, byte[] extractor,
                                                      int feat_kind, int input_size);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_train_add(IntPtr handle, ref VA_Image img, int[] roi);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_train_finish(IntPtr handle, byte[] out_summary, int cap);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_train_count(IntPtr handle, out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_train_cancel(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_create(IntPtr handle, byte[] model_dir, byte[] device);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_run(IntPtr handle, ref VA_Image img, int[] roi,
                                              float threshold, int min_area_px,
                                              ref VA_AnomStat out_stat,
                                              [In, Out] float[] out_map, int map_cap,
                                              out int out_w, out int out_h,
                                              [In, Out] byte[] out_mask, int mask_cap);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_info(IntPtr handle, byte[] buf, int cap);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_peek(byte[] model_dir, byte[] buf, int cap);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_anom_clear(IntPtr handle);

        private static byte[] Utf8Z(string s)
        {
            byte[] raw = System.Text.Encoding.UTF8.GetBytes(s ?? "");
            byte[] z = new byte[raw.Length + 1];
            Array.Copy(raw, z, raw.Length);
            return z;
        }

        private static string Ascii(byte[] buf, int n)
        {
            return n > 0 ? System.Text.Encoding.UTF8.GetString(buf, 0, n) : "";
        }

        // ---------------------- 训练 ----------------------

        /// <summary>开始一次 PaDiM 训练。featKind = <see cref="FeatOpenVino"/> 时 extractor 必须是 .xml。</summary>
        public static int TrainBegin(IntPtr handle, string outDir, string extractor,
                                     int featKind, int inputSize)
        {
            return va_anom_train_begin(handle, Utf8Z(outDir), Utf8Z(extractor), featKind, inputSize);
        }

        /// <summary>追加一张 OK 样本；roi 传 null 或宽高为 0 表示整图。</summary>
        public static int TrainAdd(IntPtr handle, ref VA_Image img, Rectangle roi)
        {
            int[] r = roi.Width > 0 && roi.Height > 0
                ? new[] { roi.X, roi.Y, roi.Width, roi.Height }
                : null;
            return va_anom_train_add(handle, ref img, r);
        }

        /// <summary>结束训练并落盘，返回原生摘要文本（失败时为空）。</summary>
        public static int TrainFinish(IntPtr handle, out string summary)
        {
            var buf = new byte[512];
            int rc = va_anom_train_finish(handle, buf, buf.Length);
            if (rc != 0) { summary = ""; return rc; }

            int n = Array.IndexOf(buf, (byte)0);
            if (n < 0) n = buf.Length;
            summary = System.Text.Encoding.UTF8.GetString(buf, 0, n);
            return rc;
        }

        public static int TrainCount(IntPtr handle)
        {
            int n;
            return va_anom_train_count(handle, out n) == 0 ? n : 0;
        }

        public static int TrainCancel(IntPtr handle)
        {
            return va_anom_train_cancel(handle);
        }

        // ---------------------- 推理 ----------------------

        /// <summary>加载模型目录。</summary>
        public static int Create(IntPtr handle, string modelDir, string device)
        {
            return va_anom_create(handle, Utf8Z(modelDir), Utf8Z(device));
        }

        /// <summary>一帧推理结果。</summary>
        public sealed class AnomRunResult
        {
            public VA_AnomStat Stat;
            /// <summary>热图（相对阈值倍数，1.0 = 阈值）；未请求时为 null。</summary>
            public float[] Heat;
            public int MapW;
            public int MapH;
            /// <summary>缺陷掩膜（0/1）；未请求时为 null。</summary>
            public byte[] Mask;
        }

        /// <summary>
        /// 推理一帧。wantHeat = false 时不算热图（只出统计，省一次全图拷贝）。
        /// </summary>
        public static AnomRunResult Run(IntPtr handle, ref VA_Image img, Rectangle roi,
                                        float threshold, int minAreaPx, bool wantHeat,
                                        out string err)
        {
            err = "";
            var result = new AnomRunResult();
            var stat = new VA_AnomStat();

            int[] r = roi.Width > 0 && roi.Height > 0
                ? new[] { roi.X, roi.Y, roi.Width, roi.Height }
                : null;

            // 先按 ROI 尺寸估个上界：ROI 未知（整图 / 越界收边）时按整图算，最坏情况够用
            int capW = roi.Width > 0 ? roi.Width : img.width;
            int capH = roi.Height > 0 ? roi.Height : img.height;
            if (capW <= 0 || capH <= 0) { err = "图像尺寸非法"; return result; }

            float[] heat = wantHeat ? new float[capW * capH] : null;
            byte[] mask = wantHeat ? new byte[capW * capH] : null;
            int w, h;

            int rc = va_anom_run(handle, ref img, r, threshold, minAreaPx, ref stat,
                                 heat, heat != null ? heat.Length : 0, out w, out h,
                                 mask, mask != null ? mask.Length : 0);

            if (rc != 0)
            {
                err = "原生返回码 " + rc;
                return result;
            }

            result.Stat = stat;

            if (heat != null && w > 0 && h > 0 && w * h <= heat.Length)
            {
                result.MapW = w;
                result.MapH = h;
                // 实际尺寸可能小于上界（ROI 被收边），按实际长度截取，避免下游按错尺寸解释
                result.Heat = new float[w * h];
                Array.Copy(heat, result.Heat, result.Heat.Length);
                if (mask != null)
                {
                    result.Mask = new byte[w * h];
                    Array.Copy(mask, result.Mask, result.Mask.Length);
                }
            }
            return result;
        }

        /// <summary>当前已加载模型的信息摘要（未加载返回空串）。</summary>
        public static string Info(IntPtr handle)
        {
            try
            {
                var buf = new byte[512];
                int n = va_anom_info(handle, buf, buf.Length);
                return Ascii(buf, n);
            }
            catch (Exception) { return ""; }
        }

        /// <summary>不加载权重，只读 model.json 给出摘要（界面列模型用）。</summary>
        public static string Peek(string modelDir)
        {
            try
            {
                var buf = new byte[384];
                int n = va_anom_peek(Utf8Z(modelDir), buf, buf.Length);
                return Ascii(buf, n);
            }
            catch (Exception) { return ""; }
        }

        public static int Clear(IntPtr handle)
        {
            return va_anom_clear(handle);
        }
    }
}
