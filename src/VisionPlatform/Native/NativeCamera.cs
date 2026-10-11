using System;
using System.Runtime.InteropServices;
using System.Text;
using VisionPlatform.Core;

namespace VisionPlatform.Native
{
    #region 与 C 端对应的结构体

    /// <summary>本机摄像头描述（对应 C 端 VA_CameraInfo）。当前只有 index 有效。</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct VA_CameraInfo
    {
        public int index;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string name;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string path;
    }

    /// <summary>打开选项（对应 C 端 VA_CameraOption）。全 0 表示设备默认。</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VA_CameraOption
    {
        public int width;
        public int height;
        public int fps;
        public int backend;    // 0=自动(MSMF 优先,回退 DShow), 1=DShow, 2=MSMF
    }

    #endregion

    /// <summary>摄像头 C 接口的 P/Invoke 声明。</summary>
    internal static class CameraNativeMethods
    {
        private const string Dll = "VisionAlgo.Native.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_camera_enum(out int out_count);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr va_camera_open(int index, ref VA_CameraOption opt);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_camera_grab(IntPtr cam, ref VA_Image dst, int timeout_ms);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_camera_get_size(IntPtr cam, out int out_w, out int out_h);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_camera_close(IntPtr cam);

        [DllImport(Dll, CallingConvention = CallingConvention.StdCall)]
        public static extern int va_camera_get_last_error(IntPtr cam, byte[] buffer, int size);
    }

    /// <summary>
    /// 本机摄像头（C++/OpenCV VideoCapture）的托管薄封装。
    /// 与 <see cref="VisionAlgo"/> 一样，所有异常都转成返回值，不向业务层抛。
    /// </summary>
    public static class CameraNative
    {
        /// <summary>枚举本机摄像头数量；失败返回 0（不抛异常）。</summary>
        public static int Enum()
        {
            try
            {
                int count;
                int rc = CameraNativeMethods.va_camera_enum(out count);
                return rc == VaResult.OK ? count : 0;
            }
            catch (Exception ex)
            {
                Logger.Warn("枚举本机摄像头失败：" + ex.Message);
                return 0;
            }
        }

        /// <summary>打开序号 index 的本机摄像头；成功返回句柄，失败返回 IntPtr.Zero。</summary>
        public static IntPtr Open(int index, int width = 0, int height = 0, int fps = 0, int backend = 0)
        {
            var opt = new VA_CameraOption
            {
                width = width,
                height = height,
                fps = fps,
                backend = backend
            };

            try
            {
                return CameraNativeMethods.va_camera_open(index, ref opt);
            }
            catch (Exception ex)
            {
                Logger.Error("打开本机摄像头失败：" + ex.Message);
                return IntPtr.Zero;
            }
        }

        /// <summary>抓一帧 BGR 到 dst；返回算法错误码（VA_OK / VA_ERR_TIMEOUT / VA_ERR_CAMERA ...）。</summary>
        public static int Grab(IntPtr cam, ref VA_Image dst, int timeoutMs)
        {
            try
            {
                return CameraNativeMethods.va_camera_grab(cam, ref dst, timeoutMs);
            }
            catch (Exception ex)
            {
                Logger.Error("抓帧异常：" + ex.Message);
                return VaResult.ErrCamera;
            }
        }

        /// <summary>查询打开后协商到的分辨率。</summary>
        public static bool GetSize(IntPtr cam, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                return CameraNativeMethods.va_camera_get_size(cam, out width, out height) == VaResult.OK
                       && width > 0 && height > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>关闭并释放句柄（幂等）。</summary>
        public static void Close(IntPtr cam)
        {
            if (cam == IntPtr.Zero) return;
            try
            {
                CameraNativeMethods.va_camera_close(cam);
            }
            catch (Exception ex)
            {
                Logger.Warn("关闭本机摄像头失败：" + ex.Message);
            }
        }

        /// <summary>取最近错误文本；无句柄或失败时返回空串。</summary>
        public static string LastError(IntPtr cam)
        {
            if (cam == IntPtr.Zero) return string.Empty;
            try
            {
                byte[] buffer = new byte[512];
                int n = CameraNativeMethods.va_camera_get_last_error(cam, buffer, buffer.Length);
                return n > 0 ? Encoding.UTF8.GetString(buffer, 0, n) : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
