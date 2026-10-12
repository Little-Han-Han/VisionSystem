using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using VisionPlatform.Core;
using VisionPlatform.Native;

namespace VisionPlatform.Camera
{
    /// <summary>
    /// 本机摄像头适配器：在 C++ 层用 OpenCV <c>VideoCapture</c> 取流，
    /// 本类只做 <see cref="ICamera"/> 契约适配（采集线程、帧转换、所有权、错误上报）。
    /// 与模拟/海康相机一样，上层只依赖 ICamera，不感知底层实现。
    /// </summary>
    public sealed class LocalCamera : ICamera
    {
        private readonly int _index;
        private readonly object _sync = new object();

        private IntPtr _handle = IntPtr.Zero;   // va_camera_open 返回；Close 时释放
        private bool _opened;
        private bool _grabbing;
        private Thread _thread;
        private volatile bool _stopFlag;
        private int _width;                     // 协商到的分辨率（打开时确定）
        private int _height;
        private bool _disposed;

        // 采集线程最小帧间隔（毫秒）：给 UI 一个约 30fps 的上限，避免消息队列积压
        private int _minFrameIntervalMs = 33;

        // ---- C3 无响应看门狗 ----
        // 原生 va_camera_grab 内部是阻塞 read（RC1，真正的超时/看门狗修复 C4 延后），
        // 相机被占用或掉线时采集线程可能永久卡在取帧里，failStreak 路径根本走不到。
        // 用一个独立定时器监控「距上次成功帧」的间隔，超过阈值即经既有 ErrorOccurred
        // 通道告知 UI，避免界面一直停在「采集中…」却毫无反馈。
        private const int NoResponseMs = 3000;            // 无响应判定阈值（毫秒）
        private System.Threading.Timer _watchdog;         // StartGrab 建，StopGrab / 采集线程退出时销
        private volatile int _lastFrameTicks;             // 最近一次成功帧的 Environment.TickCount
        private volatile bool _noResponseReported;        // 本次采集是否已上报过「无响应」（避免重复刷屏）

        // ---- C2 枚举原因 ----
        // 最近一次枚举的 USB 相机原因：枚举为 0 时由 Enumerate 记录，供界面展示，
        // 不再让用户面对「只剩模拟相机」却不知为何。
        private static string _lastEnumReason = "";

        /// <summary>以设备序号创建一个本机摄像头（尚未打开）。</summary>
        public LocalCamera(int index)
        {
            _index = index;
            Info = new CameraInfo
            {
                Id = "USB-" + index,
                Name = "本机摄像头 " + index,
                Vendor = "USB",
                Model = "",
                Serial = ""
            };
        }

        public CameraInfo Info { get; private set; }
        public bool IsOpen { get { return _opened; } }
        public bool IsGrabbing { get { return _grabbing; } }

        /// <summary>最近一次 <see cref="Enumerate"/> 的原生原因（枚举为 0 时非空，供界面展示）。</summary>
        public static string LastEnumReason { get { return _lastEnumReason; } }

        public event EventHandler<Bitmap> FrameReceived;
        public event EventHandler<string> ErrorOccurred;

        /// <summary>枚举本机摄像头（探测式，无设备时返回空列表，不抛异常）。</summary>
        public static List<CameraInfo> Enumerate()
        {
            var list = new List<CameraInfo>();

            int count = CameraNative.Enum();
            // 一台都没枚举到时，取原生给出的原因（被占用 / 无设备 / 驱动），供界面展示（C2）。
            _lastEnumReason = count <= 0 ? CameraNative.LastError(IntPtr.Zero) : "";

            for (int i = 0; i < count; i++)
            {
                list.Add(new CameraInfo
                {
                    Id = "USB-" + i,
                    Name = "本机摄像头 " + i,
                    Vendor = "USB",
                    Model = "",
                    Serial = ""
                });
            }
            return list;
        }

        public OpResult Open()
        {
            lock (_sync)
            {
                if (_opened) return OpResult.Ok("已打开");

                IntPtr h = CameraNative.Open(_index);
                if (h == IntPtr.Zero)
                {
                    // 取原生打开失败原因（无句柄，经 va_camera_get_last_error(NULL,...) 兜底），
                    // 一并告知用户，别再静默返回一句笼统失败（C2）。
                    string reason = CameraNative.LastError(IntPtr.Zero);
                    Logger.Warn("打开本机摄像头失败，序号 " + _index +
                                (string.IsNullOrEmpty(reason) ? "" : ("：" + reason)));
                    string msg = "打开本机摄像头失败：设备不存在、被其他程序占用或驱动不可用（序号 " + _index + "）。";
                    if (!string.IsNullOrEmpty(reason)) msg += " 详情：" + reason;
                    return OpResult.Fail(msg);
                }

                int w, hh;
                if (CameraNative.GetSize(h, out w, out hh) && w > 0 && hh > 0)
                {
                    _width = w;
                    _height = hh;
                    Info.Model = w + "x" + hh;
                }
                else
                {
                    // 打不开分辨率 → 后续无法分配取帧缓冲，直接失败并给出原因，
                    // 避免「打开成功却静默无显示」的难以排查现象。
                    string err = CameraNative.LastError(h);
                    CameraNative.Close(h);
                    _handle = IntPtr.Zero;
                    _opened = false;
                    Logger.Warn("本机摄像头分辨率未知，打开中止：" + err);
                    return OpResult.Fail("无法获取相机分辨率（设备未就绪或被其他程序占用）。" +
                                        (string.IsNullOrEmpty(err) ? "" : " 详情：" + err));
                }

                _handle = h;
                _opened = true;
                Logger.Info("本机摄像头已打开：" + Info);
                return OpResult.Ok();
            }
        }

        public OpResult Close()
        {
            // 1) 先停采集：最多等 1s，绝不阻塞 UI 等待 C++ 互斥量
            StopGrab();

            // 2) 立即交出句柄：UI 立刻响应，句柄交由后台线程延迟释放
            IntPtr oldHandle;
            Thread t;
            lock (_sync)
            {
                oldHandle = _handle;
                _handle = IntPtr.Zero;
                _opened = false;
                t = _thread;
            }

            Logger.Info("本机摄像头已关闭：" + Info.Id);

            if (oldHandle == IntPtr.Zero) return OpResult.Ok();

            // 3) 无残留采集线程 → 直接释放；否则后台等线程结束再释放，句柄只释放一次
            if (t == null || !t.IsAlive)
            {
                CameraNative.Close(oldHandle);
            }
            else
            {
                Thread releaser = new Thread(delegate()
                {
                    try { t.Join(5000); } catch (Exception) { }
                    CameraNative.Close(oldHandle);
                });
                releaser.IsBackground = true;
                releaser.Name = "LocalCameraClose";
                releaser.Start();
            }

            return OpResult.Ok();
        }

        public OpResult StartGrab()
        {
            lock (_sync)
            {
                if (!_opened) return OpResult.Fail("相机未打开");
                if (_grabbing) return OpResult.Ok("已在采集");

                // 上一次采集线程尚未完全退出（StopGrab 超时残留）：不再启动新线程，
                // 否则会重置 _stopFlag 造成两条采集循环并发取帧。
                if (_thread != null && _thread.IsAlive)
                    return OpResult.Fail("上一次采集尚未完全停止，请稍候再试");

                _stopFlag = false;
                _grabbing = true;

                // C3：重置无响应看门狗基准（幂等清理可能残留的旧定时器后再建新的）。
                _lastFrameTicks = Environment.TickCount;
                _noResponseReported = false;
                DisposeWatchdog();
                _watchdog = new System.Threading.Timer(WatchdogTick, null, NoResponseMs, 1000);

                _thread = new Thread(GrabLoop);
                _thread.IsBackground = true;
                _thread.Name = "LocalCameraGrab";
                _thread.Start();
                return OpResult.Ok();
            }
        }

        public OpResult StopGrab()
        {
            Thread t;
            lock (_sync)
            {
                if (!_grabbing && _thread == null) { DisposeWatchdog(); return OpResult.Ok(); }
                _grabbing = false;
                _stopFlag = true;
                t = _thread;                 // 不在此清空：由 GrabLoop 退出时清空，Close 才能判活
            }

            // C3：无论采集线程能否在 1s 内退出，都先停掉无响应看门狗，避免其继续上报。
            DisposeWatchdog();

            if (t != null && !t.Join(1000))
            {
                Logger.Warn("本机摄像头采集线程未在 1s 内结束（可能仍阻塞在取帧），句柄将延迟释放");
            }
            return OpResult.Ok();
        }

        public OpResult<Bitmap> GrabOne(int timeoutMs = 3000)
        {
            IntPtr h;
            int w, hh;
            lock (_sync)
            {
                if (!_opened) return OpResult<Bitmap>.Fail("相机未打开");
                h = _handle;
                w = _width;
                hh = _height;
            }

            if (h == IntPtr.Zero) return OpResult<Bitmap>.Fail("相机句柄无效");
            if (w <= 0 || hh <= 0) return OpResult<Bitmap>.Fail("相机分辨率未知，请重新打开相机");

            Bitmap bmp = null;
            BitmapData data = null;
            try
            {
                bmp = new Bitmap(w, hh, PixelFormat.Format24bppRgb);
                data = bmp.LockBits(new Rectangle(0, 0, w, hh),
                                    ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                VA_Image img = new VA_Image();
                img.data = data.Scan0;
                img.width = w;
                img.height = hh;
                img.channels = 3;              // BGR
                img.stride = data.Stride;      // Format24bppRgb 的 Stride 为 4 字节对齐，必须传给 C++ 

                int rc = CameraNative.Grab(h, ref img, timeoutMs);
                if (rc != VaResult.OK)
                {
                    string msg = (rc == VaResult.ErrTimeout)
                        ? "取帧超时（未获取到图像）"
                        : ("取帧失败：" + VaResult.ToString(rc));
                    bmp.UnlockBits(data);
                    data = null;
                    bmp.Dispose();
                    bmp = null;
                    return OpResult<Bitmap>.Fail(msg);
                }

                return OpResult<Bitmap>.Ok(bmp);
            }
            catch (Exception ex)
            {
                if (data != null && bmp != null) { try { bmp.UnlockBits(data); } catch (Exception) { } data = null; }
                if (bmp != null) { bmp.Dispose(); bmp = null; }
                return OpResult<Bitmap>.Fail("取帧异常：" + ex.Message);
            }
            finally
            {
                if (data != null && bmp != null) { try { bmp.UnlockBits(data); } catch (Exception) { } }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }

        /// <summary>
        /// 采集线程：循环取帧并经 <see cref="FrameReceived"/> 交出（调用方持有 Bitmap）。
        /// 无订阅者时自行释放；连续失败或设备丢失时上报并停止线程。
        /// 每轮末尾按实际耗时节流，给 UI 一个稳定上限；不影响同步的 <see cref="GrabOne"/>。
        /// </summary>
        private void GrabLoop()
        {
            int failStreak = 0;

            while (!_stopFlag)
            {
                int loopStart = Environment.TickCount;

                IntPtr h;
                int w, hh;
                lock (_sync)
                {
                    h = _handle;
                    w = _width;
                    hh = _height;
                }

                // 关闭后再进入循环（_stopFlag 已置位）时不会走到这里取帧
                if (h == IntPtr.Zero || w <= 0 || hh <= 0)
                {
                    RaiseError("相机句柄无效或分辨率未知，采集已停止");
                    break;
                }

                Bitmap frame = null;
                BitmapData data = null;
                bool fatal = false;
                try
                {
                    frame = new Bitmap(w, hh, PixelFormat.Format24bppRgb);
                    data = frame.LockBits(new Rectangle(0, 0, w, hh),
                                          ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

                    VA_Image img = new VA_Image();
                    img.data = data.Scan0;
                    img.width = w;
                    img.height = hh;
                    img.channels = 3;
                    img.stride = data.Stride;

                    int rc = CameraNative.Grab(h, ref img, 1000);
                    frame.UnlockBits(data);
                    data = null;

                    if (rc != VaResult.OK)
                    {
                        frame.Dispose();
                        frame = null;
                        failStreak++;

                        fatal = (rc == VaResult.ErrCamera) || (failStreak >= 5);
                        if (fatal)
                        {
                            string detail = CameraNative.LastError(h);
                            RaiseError((rc == VaResult.ErrTimeout ? "连续取帧超时，已停止采集"
                                        : ("取帧失败：" + VaResult.ToString(rc))) +
                                       (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")"));
                            Logger.Error("本机摄像头采集停止：" + Info.Id + " " + detail);
                        }
                    }
                    else
                    {
                        failStreak = 0;
                        _lastFrameTicks = Environment.TickCount;   // C3：拿到帧，刷新无响应基准
                        _noResponseReported = false;

                        EventHandler<Bitmap> handler = FrameReceived;
                        if (handler != null) handler(this, frame);
                        else frame.Dispose();          // 无订阅者：自行释放，避免泄漏
                        frame = null;
                    }
                }
                catch (Exception ex)
                {
                    if (data != null && frame != null) { try { frame.UnlockBits(data); } catch (Exception) { } data = null; }
                    if (frame != null) { frame.Dispose(); frame = null; }
                    RaiseError("采集异常：" + ex.Message);
                    fatal = true;
                }

                if (fatal) break;

                // 输出节流：限制 UI 刷新频率（约 1000 / _minFrameIntervalMs fps）
                int sleep = _minFrameIntervalMs - (Environment.TickCount - loopStart);
                if (sleep > 0)
                {
                    try { Thread.Sleep(sleep); }
                    catch (Exception) { }
                }
            }

            bool mine;
            lock (_sync)
            {
                // 仅当 _thread 仍指向本线程时才清空：避免误清 StopGrab 超时后新启动的采集线程
                mine = ReferenceEquals(_thread, Thread.CurrentThread);
                if (mine)
                {
                    _thread = null;
                    _grabbing = false;
                }
            }
            if (mine) DisposeWatchdog();   // C3：本采集循环结束，停掉无响应看门狗
        }

        /// <summary>
        /// C3 无响应看门狗回调：距上次成功帧超过 <see cref="NoResponseMs"/> 即经
        /// <see cref="ErrorOccurred"/> 上报一次「相机无响应」。采集已停止则不作动作。
        /// 注意：这里只负责把状态告知 UI，不尝试去中断卡在原生阻塞读里的采集线程（C4 延后）。
        /// </summary>
        private void WatchdogTick(object state)
        {
            if (!_grabbing) return;

            int elapsed = Environment.TickCount - _lastFrameTicks;
            if (elapsed >= NoResponseMs && !_noResponseReported)
            {
                _noResponseReported = true;
                RaiseError("相机无响应（可能被其他程序占用或掉线）");
                Logger.Error("本机摄像头 " + Info.Id + " 超过 " + NoResponseMs +
                             "ms 无帧（采集线程可能阻塞在取帧，见 C4）");
            }
        }

        /// <summary>安全释放无响应看门狗（幂等，可重复调用）。</summary>
        private void DisposeWatchdog()
        {
            System.Threading.Timer t = Interlocked.Exchange(ref _watchdog, null);
            if (t != null)
            {
                try { t.Dispose(); }
                catch (Exception) { }
            }
        }

        private void RaiseError(string message)
        {
            EventHandler<string> handler = ErrorOccurred;
            if (handler == null) return;
            try { handler(this, message); }
            catch (Exception) { }
        }
    }
}
