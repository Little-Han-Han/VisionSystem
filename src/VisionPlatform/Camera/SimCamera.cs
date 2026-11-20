using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using VisionPlatform.Core;

namespace VisionPlatform.Camera
{
    /// <summary>
    /// 模拟相机：在没有真实硬件时提供可跑通的图像源，
    /// 每帧生成带随机目标的合成图像，便于调试算法与界面。
    /// </summary>
    public sealed class SimCamera : ICamera
    {
        private readonly Random _rnd = new Random();
        private readonly int _width;
        private readonly int _height;
        private readonly object _sync = new object();

        private bool _opened;
        private bool _grabbing;
        private Thread _thread;
        private volatile bool _stopFlag;
        private int _frameIndex;

        public SimCamera(int width = 1280, int height = 1024)
        {
            _width = width;
            _height = height;
            Info = new CameraInfo
            {
                Id = "SIM-0",
                Name = "模拟相机",
                Vendor = "Sim",
                Model = string.Format("{0}x{1}", width, height),
                Serial = "SIM000000"
            };
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
                _opened = true;
                Logger.Info("模拟相机已打开：" + Info);
                return OpResult.Ok();
            }
        }

        public OpResult Close()
        {
            StopGrab();
            lock (_sync)
            {
                _opened = false;
            }
            Logger.Info("模拟相机已关闭");
            return OpResult.Ok();
        }

        public OpResult StartGrab()
        {
            lock (_sync)
            {
                if (!_opened) return OpResult.Fail("相机未打开");
                if (_grabbing) return OpResult.Ok("已在采集");

                _stopFlag = false;
                _grabbing = true;
                _thread = new Thread(GrabLoop);
                _thread.IsBackground = true;
                _thread.Name = "SimCameraGrab";
                _thread.Start();
                return OpResult.Ok();
            }
        }

        public OpResult StopGrab()
        {
            lock (_sync)
            {
                if (!_grabbing) return OpResult.Ok();
                _grabbing = false;
            }

            _stopFlag = true;
            if (_thread != null)
            {
                _thread.Join(1000);
                _thread = null;
            }
            return OpResult.Ok();
        }

        public OpResult<Bitmap> GrabOne(int timeoutMs = 3000)
        {
            if (!_opened) return OpResult<Bitmap>.Fail("相机未打开");
            try
            {
                return OpResult<Bitmap>.Ok(RenderFrame());
            }
            catch (Exception ex)
            {
                return OpResult<Bitmap>.Fail("取帧失败：" + ex.Message);
            }
        }

        public void Dispose()
        {
            Close();
        }

        private void GrabLoop()
        {
            PerfProbe.Mark("采集:模拟");

            while (!_stopFlag)
            {
                try
                {
                    Bitmap frame = RenderFrame();
                    EventHandler<Bitmap> handler = FrameReceived;
                    if (handler != null) handler(this, frame);
                    else frame.Dispose();
                }
                catch (Exception ex)
                {
                    EventHandler<string> err = ErrorOccurred;
                    if (err != null) err(this, ex.Message);
                }

                Thread.Sleep(100); // 约 10 FPS，模拟实时预览
            }
        }

        /// <summary>生成一帧合成图像：棋盘底纹 + 渐变 + 若干随机圆 + 噪声。</summary>
        private Bitmap RenderFrame()
        {
            int idx;
            lock (_sync) { idx = _frameIndex++; }

            var bmp = new Bitmap(_width, _height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(40, 40, 48));

                const int cell = 64;
                using (Brush checker = new SolidBrush(Color.FromArgb(58, 58, 70)))
                {
                    for (int y = 0; y < _height; y += cell)
                    {
                        for (int x = 0; x < _width; x += cell)
                        {
                            if (((x / cell) + (y / cell)) % 2 == 0)
                                g.FillRectangle(checker, x, y, cell, cell);
                        }
                    }
                }

                using (LinearGradientBrush grad = new LinearGradientBrush(
                    new Point(0, 0), new Point(_width, _height),
                    Color.FromArgb(60, 0, 120, 200), Color.FromArgb(60, 200, 120, 20)))
                {
                    g.FillRectangle(grad, 0, 0, _width, _height);
                }

                // 每一帧位置略有抖动，模拟工件移动
                int cx = _width / 2 + (int)(Math.Sin(idx * 0.15) * 120);
                int cy = _height / 2 + (int)(Math.Cos(idx * 0.11) * 90);
                int r = 110 + (int)(Math.Sin(idx * 0.07) * 18);

                using (Brush b = new SolidBrush(Color.FromArgb(200, 220, 180, 60)))
                    g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
                using (Pen p = new Pen(Color.Lime, 3))
                    g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);

                // 小目标
                for (int i = 0; i < 5; i++)
                {
                    int sx = 120 + i * 180;
                    int sy = 140 + (i % 2) * 220;
                    using (Brush b = new SolidBrush(Color.FromArgb(190, 90, 190, 240)))
                        g.FillEllipse(b, sx - 26, sy - 26, 52, 52);
                }

                // 噪声
                using (Brush noise = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                {
                    for (int i = 0; i < 1500; i++)
                        g.FillRectangle(noise, _rnd.Next(_width), _rnd.Next(_height), 1, 1);
                }

                // 帧序号
                using (Font f = new Font("Consolas", 16, FontStyle.Bold))
                using (Brush b = new SolidBrush(Color.FromArgb(180, 255, 255, 255)))
                    g.DrawString("SIM #" + idx, f, b, 12, 12);
            }

            return bmp;
        }
    }
}
