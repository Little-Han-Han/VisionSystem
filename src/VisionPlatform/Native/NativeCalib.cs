using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VisionPlatform.Native
{
    /// <summary>
    /// 相机标定的原生入口（棋盘格角点检测 / 内参+畸变标定）。
    /// <para>
    /// 对应 <c>va_calib_*</c>：角点检测在原生侧做（<c>findChessboardCorners</c> + 亚像素精化），
    /// 标定用 <c>calibrateCamera</c> —— 这两步没有纯 C# 的等价实现（要 SVD 与 LM 优化）。
    /// </para>
    /// </summary>
    public static class NativeCalib
    {
        private const string Dll = "VisionAlgo.Native.dll";

        /// <summary>角点检测：返回 [x0,y0,x1,y1,...]；失败时 <paramref name="error"/> 有可读原因。</summary>
        public static double[] FindChessboard(IntPtr handle, ref VA_Image img,
                                              int cols, int rows, int pattern, out string error)
        {
            error = "";

            int expected = cols * rows;
            if (expected <= 0) { error = "内角点行列数非法"; return null; }

            var buffer = new double[expected * 2];
            int count;

            int rc;
            try
            {
                rc = va_calib_find_chessboard(handle, ref img, cols, rows, pattern, buffer, expected, out count);
            }
            catch (Exception ex)
            {
                error = "调用角点检测失败：" + ex.Message;
                return null;
            }

            if (rc != 0 || count <= 0)
            {
                error = VaResult.ToString(rc);
                return null;
            }

            if (count * 2 > buffer.Length) count = buffer.Length / 2;

            var result = new double[count * 2];
            Array.Copy(buffer, result, result.Length);
            return result;
        }

        /// <summary>
        /// 标定：<paramref name="allPoints"/> 是各图角点首尾相接，<paramref name="counts"/> 是每张图的点数。
        /// </summary>
        public static bool Calibrate(IntPtr handle, double[] allPoints, int[] counts,
                                     int cols, int rows, int width, int height,
                                     out double[] k, out double[] dist, out double rms, out string error)
        {
            error = "";
            k = new double[9];
            dist = new double[5];
            rms = 0;

            if (allPoints == null || counts == null || counts.Length < 3)
            {
                error = "至少需要 3 张有效标定图";
                return false;
            }

            try
            {
                int rc = va_calib_calibrate(handle, allPoints, counts, counts.Length,
                                            cols, rows, width, height, 0, k, dist, out rms);
                if (rc != 0)
                {
                    error = VaResult.ToString(rc);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "调用标定失败：" + ex.Message;
                return false;
            }
        }

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_calib_find_chessboard(IntPtr handle, ref VA_Image img,
                                                           int cols, int rows, int pattern,
                                                           [In, Out] double[] out_xy, int cap,
                                                           out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        private static extern int va_calib_calibrate(IntPtr handle,
                                                     [In] double[] xy, [In] int[] counts, int n_images,
                                                     int cols, int rows, int width, int height, int flags,
                                                     [In, Out] double[] out_k,
                                                     [In, Out] double[] out_dist,
                                                     out double out_rms);
    }
}
