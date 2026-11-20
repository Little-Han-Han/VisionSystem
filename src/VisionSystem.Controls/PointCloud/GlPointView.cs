using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Pc = VisionSystem.Controls.PointCloud;

namespace VisionSystem.Controls.PointCloud
{
    /// <summary>
    /// 点云 OpenGL 视图（子控件，宿主在 <see cref="ImageViewer"/> 内按需创建，与 2D 图层互斥显示）。
    /// <para>
    /// 【为什么不用 OpenTK】缓存的 OpenTK.dll 不含 WinForms GLControl，GLControl 包下载不通；
    /// 点云显示只需要 GL 1.1 固定管线（opengl32.dll 直接导出），用 P/Invoke 自建 wgl 上下文
    /// 是零依赖且同样 GPU 加速的路径。投影/旋转用模型视图矩阵的固定管线，不需要着色器。
    /// </para>
    /// <para>
    /// 交互：左键拖动 = 三维旋转（偏航/俯仰）；滚轮 = 缩放；双击 = 复位视角。
    /// </para>
    /// </summary>
    public sealed class GlPointView : Control
    {
        // ---------------------- 状态 ----------------------

        private Pc.PointCloudData _cloud;
        private IntPtr _xyzBuffer = IntPtr.Zero;     // 顶点显存镜像（驱动侧拷贝用）
        private IntPtr _colorBuffer = IntPtr.Zero;
        private int _count;

        // 软件渲染兜底（GL 不可用 / 建上下文失败时）：CPU 投影 + 逐像素光栅，
        // 交互（旋转/缩放）与 GL 路径完全一致 —— 现场见到的"显示控件里报错"就是这里
        // 曾经只有一句红字，现在改为照常出图，原因只在 HUD 小字提示。
        private byte[] _colorsManaged;               // 3*count RGB，软件渲染用
        private Bitmap _fbBitmap;
        private int[] _fbPixels;

        private IntPtr _hglrc = IntPtr.Zero;         // wgl 渲染上下文
        private IntPtr _hdc = IntPtr.Zero;
        private string _glError;
        private string _glInfo = "";                 // GL_VENDOR / GL_RENDERER / GL_VERSION（诊断用）

        private float _yaw = 30f;                    // 偏航（绕世界 Z）
        private float _pitch = 45f;                  // 俯仰（度，钳 [-89, 89]）
        private float _distance = 10f;
        private float _defaultDistance = 10f;
        private float _cx, _cy, _cz;                 // 包围盒中心

        // 坐标轴（X/Y/Z）：原点取包围盒最小角，三轴指向各自正方向
        private bool _axesVisible = true;
        private float _ax, _ay, _az;                 // 轴原点
        private float _axLen, _ayLen, _azLen;        // 三轴长度

        private bool _dragging;
        private Point _lastMouse;

        // GL 常量（只用到 GL 1.1 固定管线）
        private const uint GL_POINTS = 0x0000;
        private const uint GL_DEPTH_TEST = 0x0B71;
        private const uint GL_COLOR_BUFFER_BIT = 0x00004000;
        private const uint GL_DEPTH_BUFFER_BIT = 0x00000100;
        private const uint GL_PROJECTION = 0x1701;
        private const uint GL_MODELVIEW = 0x1700;
        private const uint GL_FLOAT = 0x1406;
        private const uint GL_UNSIGNED_BYTE = 0x1401;
        private const uint GL_VERTEX_ARRAY = 0x8074;
        private const uint GL_COLOR_ARRAY = 0x8076;

        /// <summary>鼠标提示（HUD 文案，随状态刷新）。</summary>
        private string _hud = "点云";

        /// <summary>创建控件。</summary>
        public GlPointView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.Opaque |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);

            BackColor = Color.FromArgb(32, 32, 36);
            TabStop = false;
        }

        /// <summary>GL 上下文必须独占 DC，否则 SetPixelFormat 在重建句柄后会失效。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x0020;    // CS_OWNDC
                return cp;
            }
        }

        /// <summary>
        /// 初始化 / 渲染失败的原因（null = 正常）。宿主与诊断探针据此显示"为什么没出图"，
        /// 不再只画一句笼统的提示。
        /// </summary>
        public string GlError { get { return _glError; } }

        /// <summary>当前是否真的在用 OpenGL（false = 正在用软件渲染兜底）。</summary>
        public bool IsGlActive { get { return _glError == null && _hglrc != IntPtr.Zero; } }

        /// <summary>GL 实现信息（厂商 / 渲染器 / 版本），诊断用。</summary>
        public string GlInfo { get { return _glInfo; } }

        /// <summary>是否显示 X/Y/Z 坐标轴（默认显示）。</summary>
        public bool AxesVisible
        {
            get { return _axesVisible; }
            set { _axesVisible = value; Invalidate(); }
        }

        // ---------------------- 数据 ----------------------

        /// <summary>设置显示的点云；null 清空。视角自动适配包围盒。</summary>
        public void SetCloud(Pc.PointCloudData cloud)
        {
            _cloud = cloud;
            FreeBuffers();

            if (cloud == null || cloud.Count <= 0)
            {
                _count = 0;
                Invalidate();
                return;
            }

            _count = cloud.Count;

            // 顶点
            _xyzBuffer = Marshal.AllocHGlobal(_count * 12);
            Marshal.Copy(cloud.Xyz, 0, _xyzBuffer, _count * 3);

            // 颜色：有 RGB 用 RGB；没有按高度伪彩（蓝-青-绿-黄-红）
            byte[] colors = new byte[_count * 3];
            if (cloud.HasColor)
            {
                Array.Copy(cloud.Rgb, colors, colors.Length);
            }
            else
            {
                float zmin = cloud.ZMin, zmax = cloud.ZMax;
                float span = zmax - zmin;
                if (span <= 1e-6f) span = 1f;
                for (int i = 0; i < _count; i++)
                {
                    float t = (cloud.Xyz[i * 3 + 2] - zmin) / span;
                    Color c = HeightRamp(t);
                    colors[i * 3] = c.R;
                    colors[i * 3 + 1] = c.G;
                    colors[i * 3 + 2] = c.B;
                }
            }
            // GL 颜色缓冲用 4 字节 RGBA（A=255）：3 字节紧凑数组在部分驱动上颜色数组不生效
            //（实测表现为整片只有第一个点的颜色），4 字节对齐是所有驱动的安全格式。
            _colorBuffer = Marshal.AllocHGlobal(_count * 4);
            byte[] rgba = new byte[_count * 4];
            for (int i = 0; i < _count; i++)
            {
                rgba[i * 4] = colors[i * 3];
                rgba[i * 4 + 1] = colors[i * 3 + 1];
                rgba[i * 4 + 2] = colors[i * 3 + 2];
                rgba[i * 4 + 3] = 255;
            }
            Marshal.Copy(rgba, 0, _colorBuffer, rgba.Length);
            _colorsManaged = colors;          // 软件渲染兜底用同一套颜色

            // 视角自适应：站在包围盒斜对角方向看
            cloud.GetCenter(out _cx, out _cy, out _cz);
            float diag = cloud.Diagonal;
            if (diag <= 1e-6f) diag = 10f;
            _defaultDistance = diag * 1.6f;
            _distance = _defaultDistance;
            _yaw = 30f;
            _pitch = 45f;

            // 坐标轴：原点放包围盒最小角（点云离世界原点再远也能看到刻度尺），
            // 长度取该轴跨度的 1.2 倍（跨度退化时用对角线的 0.3 倍兜底）
            _ax = cloud.XMin; _ay = cloud.YMin; _az = cloud.ZMin;
            float fallback = diag * 0.3f;
            _axLen = AxisLength(cloud.XMax - cloud.XMin, fallback);
            _ayLen = AxisLength(cloud.YMax - cloud.YMin, fallback);
            _azLen = AxisLength(cloud.ZMax - cloud.ZMin, fallback);

            _hud = "点数 " + _count.ToString("N0") + " · 拖动=旋转 · 滚轮=缩放 · 双击=复位";
            Invalidate();
        }

        /// <summary>复位到默认视角。</summary>
        public void ResetView()
        {
            if (_cloud != null)
            {
                _distance = _defaultDistance;
                _yaw = 30f;
                _pitch = 45f;
                Invalidate();
            }
        }

        // ---------------------- 坐标轴 ----------------------

        private static float AxisLength(float span, float fallback)
        {
            float len = Math.Abs(span) * 1.2f;
            if (len <= 1e-6f) len = fallback;
            return len;
        }

        /// <summary>
        /// 世界坐标 → 屏幕像素。与 GL 模型视图矩阵、软件渲染的投影逐项一致
        /// （都是 Rz(yaw) → Rx(pitch) → 沿 Z 后退 distance → glFrustum 透视），
        /// 所以轴线/标签在两条渲染路径下落在同一位置。
        /// </summary>
        private bool ProjectToScreen(float wx, float wy, float wz, out float sx, out float sy)
        {
            sx = 0f; sy = 0f;
            int w = Math.Max(ClientSize.Width, 1);
            int h = Math.Max(ClientSize.Height, 1);

            float yawRad = _yaw * (float)(Math.PI / 180.0);
            float pitchRad = _pitch * (float)(Math.PI / 180.0);
            float cyw = (float)Math.Cos(yawRad), syw = (float)Math.Sin(yawRad);
            float cp = (float)Math.Cos(pitchRad), sp = (float)Math.Sin(pitchRad);

            float px = wx - _cx, py = wy - _cy, pz = wz - _cz;
            float x1 = px * cyw - py * syw;
            float y1 = px * syw + py * cyw;
            float y2 = y1 * cp - pz * sp;
            float z2 = y1 * sp + pz * cp;
            double vz = z2 - _distance;

            double near = Math.Max(_distance * 0.01, 0.001);
            double halfH = near * 0.5;
            double halfW = halfH * w / h;
            if (vz >= -near) return false;                  // 在相机背后 / 近面之内

            double ndcX = x1 * near / (-vz * halfW);
            double ndcY = y2 * near / (-vz * halfH);
            sx = (float)((ndcX + 1.0) * 0.5 * w);
            sy = (float)((1.0 - (ndcY + 1.0) * 0.5) * h);
            return true;
        }

        /// <summary>
        /// 画 X/Y/Z 坐标轴（GDI 叠加层，两条渲染路径共用）：
        /// 轴线用 GL 立即模式画的话要另开一套 P/Invoke，而且轴线会被点云遮挡看不清；
        /// 坐标轴本质是"参照物"，画在最上层反而更符合读数习惯，和 HUD 一样走 GDI 叠加。
        /// </summary>
        private void DrawAxesOverlay(Graphics g)
        {
            if (!_axesVisible || _cloud == null || _count <= 0) return;

            float ox, oy;
            if (!ProjectToScreen(_ax, _ay, _az, out ox, out oy)) return;

            using (var penX = new Pen(Color.FromArgb(235, 90, 90), 1.6f))
            using (var penY = new Pen(Color.FromArgb(90, 215, 110), 1.6f))
            using (var penZ = new Pen(Color.FromArgb(110, 160, 250), 1.6f))
            {
                DrawAxis(g, penX, ox, oy, _ax + _axLen, _ay, _az, "X");
                DrawAxis(g, penY, ox, oy, _ax, _ay + _ayLen, _az, "Y");
                DrawAxis(g, penZ, ox, oy, _ax, _ay, _az + _azLen, "Z");
            }
        }

        private void DrawAxis(Graphics g, Pen pen, float ox, float oy,
                              float wx, float wy, float wz, string label)
        {
            float ex, ey;
            if (!ProjectToScreen(wx, wy, wz, out ex, out ey))
                return;                                     // 端点落到相机背后，本帧不画这根

            g.DrawLine(pen, ox, oy, ex, ey);

            using (var font = new Font("微软雅黑", 8.5f, FontStyle.Bold))
            using (var brush = new SolidBrush(pen.Color))
            using (var back = new SolidBrush(Color.FromArgb(170, 20, 20, 24)))
            {
                SizeF size = g.MeasureString(label, font);
                float x = ex - size.Width / 2f;
                float y = ey - size.Height / 2f;
                g.FillRectangle(back, x - 2f, y, size.Width + 4f, size.Height);
                g.DrawString(label, font, brush, x, y);
            }
        }

        // ---------------------- 高度伪彩 ----------------------

        private static Color HeightRamp(float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            // 4 段：蓝 -> 青 -> 绿 -> 黄 -> 红
            if (t < 0.25f) return Lerp(Color.FromArgb(0, 60, 220), Color.FromArgb(0, 220, 220), t / 0.25f);
            if (t < 0.5f) return Lerp(Color.FromArgb(0, 220, 220), Color.FromArgb(40, 220, 60), (t - 0.25f) / 0.25f);
            if (t < 0.75f) return Lerp(Color.FromArgb(40, 220, 60), Color.FromArgb(240, 220, 40), (t - 0.5f) / 0.25f);
            return Lerp(Color.FromArgb(240, 220, 40), Color.FromArgb(230, 40, 30), (t - 0.75f) / 0.25f);
        }

        private static Color Lerp(Color a, Color b, float t)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        // ---------------------- wgl / GL P/Invoke ----------------------

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize;
            public ushort nVersion;
            public uint dwFlags;
            public byte iPixelType;
            public byte cColorBits;
            public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
            public byte cAlphaBits, cAlphaShift, cAccumBits;
            public byte cDepthBits, cStencilBits, cAuxBuffers;
            public byte iLayerType, bReserved;
            public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }

        [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr hdc);
        [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
        [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr hglrc);

        // ⚠ SwapBuffers 由 gdi32.dll 导出，不是 opengl32.dll！
        //   写错程序集的表现是「无法在 DLL opengl32.dll 中找到入口点」，一进渲染就抛异常
        //   （现场看到的就是"点云在显示控件里显示报错"）。
        [DllImport("gdi32.dll")] private static extern bool SwapBuffers(IntPtr hdc);

        [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int index, ref PIXELFORMATDESCRIPTOR pfd);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        [DllImport("opengl32.dll")] private static extern void glViewport(int x, int y, int w, int h);
        [DllImport("opengl32.dll")] private static extern void glMatrixMode(uint mode);
        [DllImport("opengl32.dll")] private static extern void glLoadIdentity();
        [DllImport("opengl32.dll")] private static extern void glFrustum(double l, double r, double b, double t, double n, double f);
        [DllImport("opengl32.dll")] private static extern void glRotatef(float angle, float x, float y, float z);
        [DllImport("opengl32.dll")] private static extern void glTranslatef(float x, float y, float z);
        [DllImport("opengl32.dll")] private static extern void glClearColor(float r, float g, float b, float a);
        [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
        [DllImport("opengl32.dll")] private static extern void glEnable(uint cap);
        [DllImport("opengl32.dll")] private static extern void glDisable(uint cap);
        [DllImport("opengl32.dll")] private static extern void glPointSize(float size);
        [DllImport("opengl32.dll")] private static extern void glColor3f(float r, float g, float b);
        [DllImport("opengl32.dll")] private static extern void glEnableClientState(uint cap);
        [DllImport("opengl32.dll")] private static extern void glDisableClientState(uint cap);
        [DllImport("opengl32.dll")] private static extern void glVertexPointer(int size, uint type, int stride, IntPtr ptr);
        [DllImport("opengl32.dll")] private static extern void glColorPointer(int size, uint type, int stride, IntPtr ptr);
        [DllImport("opengl32.dll")] private static extern void glDrawArrays(uint mode, int first, int count);
        [DllImport("opengl32.dll")] private static extern uint glGetError();
        [DllImport("opengl32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr glGetString(uint name);

        private const uint GL_VENDOR = 0x1F00;
        private const uint GL_RENDERER = 0x1F01;
        private const uint GL_VERSION = 0x1F02;

        private const uint PFD_DRAW_TO_WINDOW = 0x00000004;
        private const uint PFD_SUPPORT_OPENGL = 0x00000020;
        private const uint PFD_DOUBLEBUFFER = 0x00000001;

        // ---------------------- 生命周期 ----------------------

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetupGl();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            CleanupGl();
            base.OnHandleDestroyed(e);
        }

        /// <summary>释放软件渲染的位图缓冲（GL 上下文在句柄销毁时已清理）。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fbBitmap != null) { _fbBitmap.Dispose(); _fbBitmap = null; }
                _fbPixels = null;
            }
            base.Dispose(disposing);
        }

        private void SetupGl()
        {
            _glError = null;

            // 诊断/规避用：VA_FORCE_SOFT_RENDER=1 强制走软件渲染（远程桌面、虚拟机验证用）
            string force = Environment.GetEnvironmentVariable("VA_FORCE_SOFT_RENDER");
            if (!string.IsNullOrEmpty(force) && force != "0")
            {
                _glError = "已按 VA_FORCE_SOFT_RENDER 强制软件渲染";
                return;
            }

            _hdc = GetDC(Handle);
            if (_hdc == IntPtr.Zero) { _glError = "取 DC 失败"; return; }

            // 候选像素格式依次尝试：双缓冲+深度 → 双缓冲无深度 → 单缓冲+深度。
            // SetPixelFormat 成功后就锁定，不能再换；失败则可以试下一个候选。
            var candidates = new[]
            {
                new PixelFormatRequest { DoubleBuffer = true,  DepthBits = 16 },
                new PixelFormatRequest { DoubleBuffer = true,  DepthBits = 0  },
                new PixelFormatRequest { DoubleBuffer = false, DepthBits = 16 }
            };

            bool formatOk = false;
            string lastReason = "设置像素格式失败（远程桌面 / 无 GPU 驱动环境常见）";

            foreach (PixelFormatRequest req in candidates)
            {
                var pfd = new PIXELFORMATDESCRIPTOR();
                pfd.nSize = (ushort)Marshal.SizeOf(typeof(PIXELFORMATDESCRIPTOR));
                pfd.nVersion = 1;
                pfd.dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL;
                if (req.DoubleBuffer) pfd.dwFlags |= PFD_DOUBLEBUFFER;
                pfd.iPixelType = 1;             // PFD_TYPE_RGBA
                pfd.cColorBits = 24;
                pfd.cDepthBits = (byte)req.DepthBits;

                int index = ChoosePixelFormat(_hdc, ref pfd);
                if (index <= 0) continue;

                if (SetPixelFormat(_hdc, index, ref pfd))
                {
                    formatOk = true;
                    break;
                }
            }

            if (!formatOk)
            {
                _glError = lastReason;
                return;
            }

            _hglrc = wglCreateContext(_hdc);
            if (_hglrc == IntPtr.Zero) { _glError = "创建 OpenGL 上下文失败"; return; }

            if (!wglMakeCurrent(_hdc, _hglrc))
            {
                _glError = "绑定 OpenGL 上下文失败";
                try { wglDeleteContext(_hglrc); } catch (Exception) { }
                _hglrc = IntPtr.Zero;
                return;
            }

            _glInfo = ReadGlString(GL_VENDOR) + " / " + ReadGlString(GL_RENDERER) + " / " + ReadGlString(GL_VERSION);

            // 判定"这个 GL 是不是真的 GPU"：没有显卡驱动时 Windows 只给 GDI Generic
            // （纯 CPU 模拟的老式 GL 1.1），点大小、颜色数组都只能凑合用。
            // 现场工控机 / 远程桌面 / 虚拟机全是这种情况 —— 既然我们自己有逐点光栅的
            // 软件渲染（颜色/点大小完全可控），就优先用它的确定性，不用系统模拟的 GL。
            if (IsSoftwareGl(_glInfo))
            {
                _glError = "系统 GL 为软件模拟（" + _glInfo + "），改用内置渲染";
                // 只回收 GL 上下文，不动顶点/颜色缓冲（SetCloud 可能已经灌好数据，
                // 软件渲染路径还要用 _colorsManaged）
                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                wglDeleteContext(_hglrc);
                _hglrc = IntPtr.Zero;
                return;
            }

            glEnable(GL_DEPTH_TEST);
            Invalidate();
        }

        private static string ReadGlString(uint name)
        {
            try
            {
                IntPtr p = glGetString(name);
                return p == IntPtr.Zero ? "?" : (Marshal.PtrToStringAnsi(p) ?? "?").Trim();
            }
            catch (Exception) { return "?"; }
        }

        /// <summary>GDI Generic（微软的软件 GL）不适合做点云显示。</summary>
        private static bool IsSoftwareGl(string info)
        {
            if (string.IsNullOrEmpty(info)) return false;
            string s = info.ToLowerInvariant();
            return s.Contains("gdi generic")
                || s.Contains("microsoft corporation /  /")
                || (s.Contains("microsoft") && s.Contains("1.1.0"));
        }

        /// <summary>像素格式候选（见 SetupGl 的依次尝试）。</summary>
        private struct PixelFormatRequest
        {
            public bool DoubleBuffer;
            public int DepthBits;
        }

        private void CleanupGl()
        {
            if (_hglrc != IntPtr.Zero)
            {
                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                wglDeleteContext(_hglrc);
                _hglrc = IntPtr.Zero;
            }
            if (_hdc != IntPtr.Zero)
            {
                ReleaseDC(Handle, _hdc);
                _hdc = IntPtr.Zero;
            }
            FreeBuffers();
        }

        private void FreeBuffers()
        {
            if (_xyzBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(_xyzBuffer); _xyzBuffer = IntPtr.Zero; }
            if (_colorBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(_colorBuffer); _colorBuffer = IntPtr.Zero; }
            _colorsManaged = null;
        }

        // ---------------------- 渲染 ----------------------

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // GL 自己清屏，GDI 不再擦（避免闪烁）
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // 空点云：给一句引导，不留黑屏
            if (_count <= 0 || _cloud == null)
            {
                using (var b = new SolidBrush(BackColor)) e.Graphics.FillRectangle(b, ClientRectangle);
                using (var font = new Font("微软雅黑", 10f))
                    e.Graphics.DrawString("读取点云后在这里显示（左键拖动旋转 · 滚轮缩放 · 双击复位）",
                        font, Brushes.Gray, 14, 14);
                return;
            }

            bool glOk = false;
            try { glOk = RenderGl(e.Graphics); }
            catch (Exception ex)
            {
                _glError = "OpenGL 渲染异常：" + ex.Message;
                glOk = false;
            }

            if (!glOk) RenderSoftware(e.Graphics);

            DrawAxesOverlay(e.Graphics);     // 坐标轴（两条路径共用 GDI 叠加，见方法注释）
            DrawHud(e.Graphics);
        }

        /// <summary>GL 路径渲染；返回 false 表示本次不可用（由调用方转软件渲染）。</summary>
        private bool RenderGl(Graphics g)
        {
            if (_glError != null || _hglrc == IntPtr.Zero) return false;
            if (!wglMakeCurrent(_hdc, _hglrc)) return false;

            try
            {
                int w = Math.Max(ClientSize.Width, 1);
                int h = Math.Max(ClientSize.Height, 1);
                glViewport(0, 0, w, h);

                glClearColor(0.125f, 0.125f, 0.14f, 1f);       // 与 2D 视图背景一致
                glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

                // 透视投影（近面按点云尺度缩放，避免极端点距下被裁）
                glMatrixMode(GL_PROJECTION);
                glLoadIdentity();
                double near = Math.Max(_distance * 0.01, 0.001);
                double far = _distance * 100.0 + 1000.0;
                double halfH = near * 0.5;
                double halfW = halfH * w / h;
                glFrustum(-halfW, halfW, -halfH, halfH, near, far);

                glMatrixMode(GL_MODELVIEW);
                glLoadIdentity();
                glTranslatef(0f, 0f, -_distance);
                glRotatef(_pitch, 1f, 0f, 0f);
                glRotatef(_yaw, 0f, 0f, 1f);
                glTranslatef(-_cx, -_cy, -_cz);

                if (_xyzBuffer != IntPtr.Zero)
                {
                    // 点大小按视口高粗略自适应（1% 高度，钳 1~3px）
                    glPointSize(Math.Max(1f, Math.Min(3f, h * 0.01f)));

                    glEnableClientState(GL_VERTEX_ARRAY);
                    glVertexPointer(3, GL_FLOAT, 0, _xyzBuffer);

                    // 注意：GL_COLOR_ARRAY 一旦启用就逐顶点取样，glColor3f 会被完全忽略，
                    // 所以两条分支必须显式互斥地 启用/禁用 颜色数组（不能只改"设不设"）。
                    string whiteTest = Environment.GetEnvironmentVariable("VA_GL_COLOR_WHITE");
                    if (whiteTest == "1" || _colorBuffer == IntPtr.Zero)
                    {
                        glDisableClientState(GL_COLOR_ARRAY);
                        glColor3f(1f, 1f, 1f);          // VA_GL_COLOR_WHITE=1 的诊断对照：全白点
                    }
                    else
                    {
                        glEnableClientState(GL_COLOR_ARRAY);
                        glColorPointer(4, GL_UNSIGNED_BYTE, 0, _colorBuffer);   // 4 字节 RGBA，见 SetCloud
                    }
                    glDrawArrays(GL_POINTS, 0, _count);
                    glDisableClientState(GL_COLOR_ARRAY);
                }

                SwapBuffers(_hdc);

                uint err = glGetError();
                if (err != 0 && _hud.IndexOf("GL错误", StringComparison.Ordinal) < 0)
                    _hud += " （GL错误 " + err.ToString("X") + "）";

                return true;
            }
            finally
            {
                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            }
        }

        /// <summary>
        /// 软件渲染：与 GL 完全相同的旋转/透视参数，逐点投影到内存位图。
        /// 只要 CPU 能算就能出图 —— 远程桌面、虚拟机、无 GL 驱动的现场也不至于"什么都不显示"。
        /// </summary>
        private void RenderSoftware(Graphics g)
        {
            int w = Math.Max(ClientSize.Width, 1);
            int h = Math.Max(ClientSize.Height, 1);

            if (_fbBitmap == null || _fbBitmap.Width != w || _fbBitmap.Height != h || _fbPixels == null
                || _fbPixels.Length != w * h)
            {
                if (_fbBitmap != null) _fbBitmap.Dispose();
                _fbBitmap = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                _fbPixels = new int[w * h];
            }

            // 背景填充（0x00RRGGBB；WPF/GL 都是这个通道顺序）
            int bg = (BackColor.R) | (BackColor.G << 8) | (BackColor.B << 16);
            for (int i = 0; i < _fbPixels.Length; i++) _fbPixels[i] = bg;

            int visible = 0;
            if (_count > 0 && _cloud != null && _colorsManaged != null)
            {
                float yawRad = _yaw * (float)(Math.PI / 180.0);
                float pitchRad = _pitch * (float)(Math.PI / 180.0);
                float cyw = (float)Math.Cos(yawRad), syw = (float)Math.Sin(yawRad);
                float cp = (float)Math.Cos(pitchRad), sp = (float)Math.Sin(pitchRad);

                double near = Math.Max(_distance * 0.01, 0.001);
                double halfH = near * 0.5;
                double halfW = halfH * w / h;
                int dot = (int)Math.Max(1, Math.Min(3, h * 0.01f));

                float[] xyz = _cloud.Xyz;
                byte[] colors = _colorsManaged;

                for (int i = 0; i < _count; i++)
                {
                    float px = xyz[i * 3] - _cx;
                    float py = xyz[i * 3 + 1] - _cy;
                    float pz = xyz[i * 3 + 2] - _cz;

                    // Rz(yaw) → Rx(pitch) → 平移(0,0,-d)，与 GL 的模型视图矩阵同序
                    float x1 = px * cyw - py * syw;
                    float y1 = px * syw + py * cyw;
                    float y2 = y1 * cp - pz * sp;
                    float z2 = y1 * sp + pz * cp;
                    double vz = z2 - _distance;

                    if (vz >= -near) continue;              // 近面之后 / 相机背后

                    double ndcX = x1 * near / (-vz * halfW);
                    double ndcY = y2 * near / (-vz * halfH);
                    int sx = (int)((ndcX + 1.0) * 0.5 * w);
                    int sy = (int)((1.0 - (ndcY + 1.0) * 0.5) * h);
                    if (sx < 0 || sx >= w || sy < 0 || sy >= h) continue;

                    int rgb = colors[i * 3] | (colors[i * 3 + 1] << 8) | (colors[i * 3 + 2] << 16);

                    for (int dy = 0; dy < dot; dy++)
                    {
                        int yy = sy + dy;
                        if (yy >= h) break;
                        int row = yy * w;
                        for (int dx = 0; dx < dot; dx++)
                        {
                            int xx = sx + dx;
                            if (xx >= w) break;
                            _fbPixels[row + xx] = rgb;
                        }
                    }
                    visible++;
                }
            }

            // 直接写位图（LockBits + 行拷贝：几十万点也只是一次内存拷贝）
            var rect = new Rectangle(0, 0, w, h);
            System.Drawing.Imaging.BitmapData bd = _fbBitmap.LockBits(rect,
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(_fbPixels, y * w, (IntPtr)((long)bd.Scan0 + (long)y * bd.Stride), w);
            }
            finally
            {
                _fbBitmap.UnlockBits(bd);
            }

            g.DrawImageUnscaled(_fbBitmap, 0, 0);

            if (visible == 0 && _count > 0)
            {
                using (var font = new Font("微软雅黑", 9.5f))
                    g.DrawString("当前视角看不到点（滚轮缩小或双击复位）", font, Brushes.Gray, 14, 14);
            }
        }

        private void DrawHud(Graphics g)
        {
            if (string.IsNullOrEmpty(_hud)) return;

            string text = _hud;
            if (!IsGlActive) text += "　·　软件渲染";   // GL 不可用时给个小标记，不当错误弹

            using (var font = new Font("微软雅黑", 9f))
            using (var back = new SolidBrush(Color.FromArgb(160, 24, 24, 28)))
            {
                SizeF size = g.MeasureString(text, font);
                var rect = new Rectangle(8, ClientSize.Height - (int)size.Height - 8, (int)size.Width + 12, (int)size.Height + 4);
                g.FillRectangle(back, rect);
                g.DrawString(text, font, Brushes.Gainsboro, rect.X + 6, rect.Y + 2);
            }
        }

        // ---------------------- 交互 ----------------------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _lastMouse = e.Location;
                Focus();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || _cloud == null) return;

            int dx = e.X - _lastMouse.X;
            int dy = e.Y - _lastMouse.Y;
            _lastMouse = e.Location;

            _yaw += dx * 0.4f;
            _pitch += dy * 0.4f;
            if (_pitch > 89f) _pitch = 89f;
            if (_pitch < -89f) _pitch = -89f;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) _dragging = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_cloud == null) return;

            float factor = e.Delta > 0 ? 0.88f : 1.14f;
            float min = Math.Max(_defaultDistance * 0.02f, 0.001f);
            float max = _defaultDistance * 30f;
            _distance = Math.Max(min, Math.Min(max, _distance * factor));
            Invalidate();
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            ResetView();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // OnResize 里只 Invalidate，不动子控件（本控件没有子控件，但保持惯例判空/防御）
            Invalidate();
        }
    }
}
