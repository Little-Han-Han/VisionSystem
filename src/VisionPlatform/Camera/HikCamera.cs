using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using VisionPlatform.Core;

namespace VisionPlatform.Camera
{
    /// <summary>
    /// 海康威视（Hikrobot）工业相机适配器。
    ///
    /// 【当前状态 —— 需要补全】
    /// 本机尚未安装海康 MVS SDK，因此这里只完成了「框架 + SDK 探测」：
    ///   - <see cref="HikSdk"/> 负责探测 MvCameraControl.Net.dll 是否可用
    ///   - 本类实现完整的 ICamera 契约，Open/StartGrab 在 SDK 缺失时返回明确错误
    ///   - 真实取流需要用 SDK 的 MyCamera 替换标记为 TODO 的分支
    /// 安装 MVS 后（默认路径 C:\Program Files (x86)\MVS），
    /// 在项目里添加对 MvCameraControl.Net.dll 的引用，即可补全 TODO 部分。
    /// </summary>
    public sealed class HikCamera : ICamera
    {
        private readonly object _sync = new object();
        private bool _opened;
        private bool _grabbing;

        /// <summary>SDK 提供的设备对象（反射持有，避免编译期依赖）。</summary>
        private object _device;

        public HikCamera(CameraInfo info)
        {
            Info = info ?? new CameraInfo { Vendor = "Hik" };
            if (string.IsNullOrEmpty(Info.Vendor)) Info.Vendor = "Hik";
        }

        public CameraInfo Info { get; private set; }
        public bool IsOpen { get { return _opened; } }
        public bool IsGrabbing { get { return _grabbing; } }

        public event EventHandler<Bitmap> FrameReceived;
        public event EventHandler<string> ErrorOccurred;

        public OpResult Open()
        {
            lock (_sync)
            {
                if (_opened) return OpResult.Ok("已打开");

                string sdkError;
                if (!HikSdk.TryLoad(out sdkError))
                {
                    Logger.Warn("海康 SDK 不可用：" + sdkError);
                    return OpResult.Fail("未检测到海康 MVS SDK（" + sdkError + "）。" +
                                         "请安装 MVS 并在项目中引用 MvCameraControl.Net.dll 后重试。");
                }

                // TODO(SDK): 用 MvCameraControl 的 MyCamera 打开设备
                //   参考流程：MV_CC_CreateDeviceInfoList -> MV_CC_OpenDevice
                //            -> MV_CC_SetEnumValue(TriggerMode) -> MV_CC_StartGrabbing
                // 在 SDK 就位后，把下面的占位分支替换为真实调用。
                _device = null;
                _opened = true;
                Logger.Info("海康相机已打开（占位实现）：" + Info);
                return OpResult.Ok("已打开（占位实现，未接真实取流）");
            }
        }

        public OpResult Close()
        {
            StopGrab();
            lock (_sync)
            {
                // TODO(SDK): MV_CC_StopGrabbing / MV_CC_CloseDevice / MV_CC_DestroyHandle
                _device = null;
                _opened = false;
            }
            return OpResult.Ok();
        }

        public OpResult StartGrab()
        {
            lock (_sync)
            {
                if (!_opened) return OpResult.Fail("相机未打开");
                if (_grabbing) return OpResult.Ok("已在采集");

                // TODO(SDK): MV_CC_StartGrabbing + 取流线程
                _grabbing = false;
                return OpResult.Fail("海康取流尚未接入 SDK：请先安装 MVS 并补全 HikCamera 中的 TODO 分支。");
            }
        }

        public OpResult StopGrab()
        {
            lock (_sync)
            {
                _grabbing = false;
            }
            return OpResult.Ok();
        }

        public OpResult<Bitmap> GrabOne(int timeoutMs = 3000)
        {
            if (!_opened) return OpResult<Bitmap>.Fail("相机未打开");
            return OpResult<Bitmap>.Fail("海康取流尚未接入 SDK：请先安装 MVS 并补全 HikCamera 中的 TODO 分支。");
        }

        /// <summary>
        /// 枚举在线的海康相机。SDK 不可用时返回空列表。
        /// </summary>
        public static List<CameraInfo> Enumerate()
        {
            var list = new List<CameraInfo>();

            string sdkError;
            if (!HikSdk.TryLoad(out sdkError))
            {
                Logger.Warn("跳过海康枚举：" + sdkError);
                return list;
            }

            // TODO(SDK): MV_CC_EnumDevices(MV_GIGE_DEVICE | MV_USB_DEVICE, ref list)
            // 解析 MV_CC_DEVICE_INFO 得到序列号 / 型号 / IP，填充 CameraInfo。
            return list;
        }

        private void RaiseError(string message)
        {
            EventHandler<string> handler = ErrorOccurred;
            if (handler != null) handler(this, message);
        }

        public void Dispose()
        {
            Close();
        }
    }

    /// <summary>海康 MVS SDK 探测：只做文件检查与（可选）反射加载，不产生编译期依赖。</summary>
    public static class HikSdk
    {
        /// <summary>MvCameraControl.Net.dll 的常见安装位置。</summary>
        public static readonly string[] CandidatePaths =
        {
            @"C:\Program Files (x86)\MVS\Development\DotNet\MvCameraControl.Net.dll",
            @"C:\Program Files\MVS\Development\DotNet\MvCameraControl.Net.dll",
            @"D:\Program Files (x86)\MVS\Development\DotNet\MvCameraControl.Net.dll",
            @"D:\Program Files\MVS\Development\DotNet\MvCameraControl.Net.dll"
        };

        private static bool _resolved;
        private static string _path;
        private static Assembly _assembly;

        /// <summary>SDK 是否可用。</summary>
        public static bool IsAvailable
        {
            get
            {
                string err;
                return TryLoad(out err);
            }
        }

        /// <summary>已解析到的 SDK 路径（不可用时为 null）。</summary>
        public static string ResolvedPath { get { return _path; } }

        /// <summary>尝试定位并加载 SDK。失败时通过 error 给出原因。</summary>
        public static bool TryLoad(out string error)
        {
            if (_resolved)
            {
                error = null;
                return _assembly != null;
            }

            _resolved = true;

            foreach (string path in CandidatePaths)
            {
                if (!File.Exists(path)) continue;

                try
                {
                    _assembly = Assembly.LoadFrom(path);
                    _path = path;
                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    error = "加载 " + path + " 失败：" + ex.Message;
                    return false;
                }
            }

            error = "未找到 MvCameraControl.Net.dll（已查找 " + CandidatePaths.Length + " 个常见路径）";
            return false;
        }
    }
}
