using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using VisionSystem.Controls.Geometry;
using VisionSystem.Controls.Interop;
using VisionSystem.Controls.Rendering;

namespace VisionSystem.Controls
{
    /// <summary>控件的鼠标交互模式。</summary>
    public enum InteractionMode
    {
        /// <summary>选择并编辑已有图形；空白处按下可取消选择。</summary>
        Select = 0,

        /// <summary>平移画布（任何按键拖拽都是平移）。</summary>
        Pan = 1,

        /// <summary>拖拽绘制圆形：按下定圆心，拖动定半径。</summary>
        DrawCircle = 2,

        /// <summary>拖拽绘制轴对齐矩形。</summary>
        DrawRectangle = 3,

        /// <summary>拖拽绘制旋转矩形（先画成 0 度，再用旋转手柄调整角度）。</summary>
        DrawRotatedRect = 4,

        /// <summary>拖拽绘制圆环：按下定圆心，拖动定外半径，内半径默认取外半径的一半。</summary>
        DrawRing = 5,

        /// <summary>拖拽绘制线段：按下定起点，拖动定终点（直线卡尺的扫描线）。</summary>
        DrawLine = 6
    }

    /// <summary>携带单个图形的事件参数。</summary>
    public class ShapeEventArgs : EventArgs
    {
        /// <summary>相关图形。</summary>
        public IGeometryShape Shape { get; private set; }

        /// <summary>创建事件参数。</summary>
        public ShapeEventArgs(IGeometryShape shape)
        {
            Shape = shape;
        }
    }

    /// <summary>携带鼠标位置（同时给出图像坐标与控件坐标）的事件参数。</summary>
    public class ImagePointEventArgs : EventArgs
    {
        /// <summary>鼠标所在的图像坐标（可能为图像范围外的值）。</summary>
        public PointF ImagePoint { get; private set; }

        /// <summary>鼠标所在的控件坐标。</summary>
        public Point ControlPoint { get; private set; }

        /// <summary>该位置的像素颜色；若超出图像范围或取色失败则为 null。</summary>
        public Color? PixelColor { get; private set; }

        /// <summary>创建事件参数。</summary>
        public ImagePointEventArgs(PointF imagePoint, Point controlPoint, Color? pixelColor)
        {
            ImagePoint = imagePoint;
            ControlPoint = controlPoint;
            PixelColor = pixelColor;
        }
    }

    /// <summary>
    /// 机器视觉图像显示控件：支持鼠标滚轮缩放、拖拽平移、绘制与编辑
    /// 圆形 / 矩形 / 旋转矩形等几何图形，并可与 OpenCV 的 <c>Mat</c> 直接互通。
    /// </summary>
    [ToolboxItem(true)]
    [Description("机器视觉图像显示控件（缩放/平移/几何图形标注）")]
    public class ImageViewer : Control
    {
        #region 字段

        private Bitmap _image;

        // 传入的图可能是索引色（8bpp 灰度很常见），GDI+ 缩放绘制索引位图极慢，
        // 因此显示前统一转成 24bpp。_imageIsGray 记录「转换前」的灰度判定结果，
        // 保证 HUD 仍然按灰度显示。
        private bool _imageIsGray;
        private bool _ownsDisplayImage;

        // 图像金字塔：逐级 1/2 下采样（第 i 级缩放为 1/2^(i+1)）。
        // 只在图像更换时生成一次；缩放时只是「换一层」来采样，不再重新缩整图，
        // 所以滚轮连续缩放不会每档都卡一下。
        private List<Bitmap> _pyramid;
        private bool _pyramidDirty = true;

        /// <summary>连续换图（实时预览 / 连续运行）时为 true：用便宜插值。</summary>
        private bool _liveStream;

        /// <summary>上次 SetImage 的时间戳（Stopwatch 计），仅用于判断是否连续流。</summary>
        private long _lastImageStamp;

        /// <summary>
        /// 当前这张图已经被绘制了几次（每次真正 DrawImage 累加，换图时归零）。
        /// <para>【为什么用它决定要不要建金字塔】金字塔只有在"同一张图被反复重绘"（缩放 / 平移 /
        /// 暂停检查）时才划算。而流程慢的时候（实测单圈 0.6~1.1 秒），每帧间隔远大于 300ms，
        /// 用"帧间隔"判定连续流会把这种场景误判成"静态图"，于是**每帧都白建一遍金字塔**——
        /// 现场实测建层耗时 620~833ms/帧，比算法本身还贵。
        /// 改成看"同一张图第几次被画"：第一遍直接采样，只有真的要重画第二遍才建层。</para>
        /// </summary>
        private int _paintsOfCurrentImage;

        /// <summary>
        /// 当前是否处于「连续流」：既要刚才判定为连续换图，且距上一次换图没多久。
        /// <para>后半句必不可少 —— 流程停下后不再有 SetImage，但用户还会缩放/平移反复重绘；
        /// 那时必须回到高质量插值 + 金字塔，否则静止画面会一直是实时预览的低画质。</para>
        /// </summary>
        private bool IsLiveStreaming
        {
            get
            {
                if (!_liveStream || _lastImageStamp == 0) return false;
                double sinceMs = (Stopwatch.GetTimestamp() - _lastImageStamp) * 1000.0 / Stopwatch.Frequency;
                return sinceMs < LiveStreamThresholdMs;
            }
        }

        // 静态内容缓存层：背景 + 图像 + 像素网格。
        // 只在图像、缩放/平移、控件尺寸变化时重建；鼠标移动、拖拽图形时可直接复用，
        // 从而避免每帧重做大图的缩放绘制——这是交互流畅度的关键。
        private Bitmap _imageLayer;
        private bool _imageLayerDirty = true;

        private readonly Viewport _viewport = new Viewport();
        private readonly ShapeCollection _shapes = new ShapeCollection();
        private readonly RenderOptions _renderOptions = new RenderOptions();

        private InteractionMode _mode = InteractionMode.Select;
        private float _zoomStep = 1.15f;
        private bool _ownsImage = true;
        private bool _autoFitOnResize;

        // ---------- 点云模式（3D） ----------
        // 与 2D 图层互斥：SetPointCloud 进入 3D（GL 子面板盖满客户区），SetImage 退出回 2D。
        // 合并显示 = 用户始终面对同一个 ImageViewer，2D/3D 由数据类型自动切换。
        private PointCloud.GlPointView _glView;
        private bool _cloudMode;
        private PointCloud.PointCloudData _cloud;

        private Color _newShapeColor = Color.Lime;
        private float _newShapeLineWidth = 2f;

        // 交互状态
        private bool _panning;
        private Point _panStartScreen;
        private float _panStartOffsetX;
        private float _panStartOffsetY;

        private bool _drawing;
        private PointF _drawStartImage;
        private Point _drawStartScreen;
        private IGeometryShape _previewShape;

        private bool _draggingShape;
        private IGeometryShape _activeShape;
        private int _activeHandleIndex = -1;
        private PointF _lastImagePoint;

        // 右键菜单
        private ContextMenuStrip _contextMenu;
        private ToolStripMenuItem _menuSaveImage;
        private ToolStripMenuItem _menuSaveSnapshot;
        private ToolStripMenuItem _menuFitImage;
        private ToolStripMenuItem _menuCrosshair;
        private bool _rightButtonDown;
        private bool _rightDragged;

        private bool _spaceHeld;
        private PointF _mouseImagePoint;
        private Color? _mousePixelColor;
        private Point _mouseControlPoint = Point.Empty;
        private bool _mouseInside;

        /// <summary>拖拽绘制时，小于该屏幕距离视为误操作，不生成图形。</summary>
        private const float MinDrawDistancePx = 4f;

        /// <summary>右键按下后移动超过该屏幕距离即判定为平移，抬起时不再弹出菜单。</summary>
        private const int MenuDragThresholdPx = 4;

        /// <summary>可见源区域像素数超过该值时，改用金字塔层采样并降级插值质量。</summary>
        private const long LargeSourcePixelThreshold = 2000000L;

        /// <summary>
        /// 两次换图间隔小于该值即视为「连续流」（实时预览 / 连续运行）。
        /// <para>实测（5MP 24bpp → 1224×1024，GDI+ 软件光栅）：HighQuality 双线性 39ms、
        /// 普通双线性 17.6ms、最近邻 7.7ms。也就是说"每帧重建金字塔"光重采样就吃掉 39ms，
        /// 比它省下的采样量贵得多 —— 连续流下必须跳过金字塔，直接按屏幕尺寸采样。</para>
        /// </summary>
        private const int LiveStreamThresholdMs = 300;

        /// <summary>金字塔最大层数。</summary>
        private const int MaxPyramidLevels = 12;

        /// <summary>金字塔层的宽或高小于该值时停止继续下采样。</summary>
        private const int MinPyramidSize = 32;

        /// <summary>命中测试在屏幕上放大的容差（像素）。</summary>
        private const float HitTolerancePx = 5f;

        #endregion

        #region 构造与生命周期

        /// <summary>创建控件。</summary>
        public ImageViewer()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);

            BackColor = Color.FromArgb(32, 32, 36);
            TabStop = true;
            Cursor = Cursors.Default;

            _shapes.CollectionChanged += OnShapesCollectionChanged;
            BuildContextMenu();
        }

        /// <summary>释放资源。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _shapes.CollectionChanged -= OnShapesCollectionChanged;
                if (_contextMenu != null)
                {
                    _contextMenu.Dispose();
                    _contextMenu = null;
                }
                if (_imageLayer != null)
                {
                    _imageLayer.Dispose();
                    _imageLayer = null;
                }
                ClearPyramid();
                if (_ownsDisplayImage && _image != null)
                {
                    _image.Dispose();
                    _image = null;
                }
                if (_glView != null)
                {
                    _glView.Dispose();
                    _glView = null;
                }
            }
            base.Dispose(disposing);
        }

        #endregion

        #region 事件

        /// <summary>缩放倍率发生变化。</summary>
        public event EventHandler ZoomChanged;

        /// <summary>图形集合发生变化（增删改或几何被修改）。</summary>
        public event EventHandler ShapesChanged;

        /// <summary>选中的图形发生变化。</summary>
        public event EventHandler SelectionChanged;

        /// <summary>用户通过拖拽新建了一个图形。</summary>
        public event EventHandler<ShapeEventArgs> ShapeCreated;

        /// <summary>
        /// 用户<strong>调整完一个图形并松手</strong>（拖动结束）。与 <see cref="ShapeCreated"/>
        /// 的分工：后者只在「新画出来一次」触发，本事件每次拖动 / 缩放 / 旋转结束都触发。
        /// <para>宿主据此<strong>落盘一次</strong>；拖动过程中只靠 <see cref="ShapesChanged"/> 刷预览，
        /// 绝不每帧写配置（否则每帧都 SaveFlows）。</para>
        /// </summary>
        public event EventHandler<ShapeEventArgs> ShapeCommitted;

        /// <summary>显示的图像被替换。</summary>
        public event EventHandler ImageChanged;

        /// <summary>鼠标在控件上移动，携带图像坐标与像素值。</summary>
        public event EventHandler<ImagePointEventArgs> MouseImageMove;

        #endregion

        #region 属性

        /// <summary>当前显示的图像。赋值等价于调用 <see cref="SetImage(Bitmap, bool)"/>。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Bitmap Image
        {
            get { return _image; }
            set { SetImage(value); }
        }

        /// <summary>图形集合。直接增删改会自动触发重绘。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ShapeCollection Shapes
        {
            get { return _shapes; }
        }

        /// <summary>渲染选项（背景色、网格、控制点外观等）。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public RenderOptions Options
        {
            get { return _renderOptions; }
        }

        /// <summary>视口（缩放/平移状态与坐标换算）。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Viewport Viewport
        {
            get { return _viewport; }
        }

        /// <summary>
        /// 结果标签字号（屏幕像素，不随缩放变化）；<c>&lt;= 0</c> 不画标签。
        /// <para>目标很多（如 Blob 20 个）时可调小或关掉，避免标签糊满画面。</para>
        /// </summary>
        [Category("Appearance"), DefaultValue(8f)]
        [Description("图形结果标签的字号（屏幕像素）；小于等于 0 表示不显示标签。")]
        public float ShapeLabelFontSize
        {
            get { return _renderOptions.LabelFontSize; }
            set
            {
                if (Math.Abs(_renderOptions.LabelFontSize - value) < 0.01f) return;
                _renderOptions.LabelFontSize = value;
                Invalidate();
            }
        }

        /// <summary>当前交互模式。</summary>
        [Category("Behavior"), DefaultValue(InteractionMode.Select)]
        [Description("鼠标交互模式：选择编辑 / 平移 / 绘制各类图形。")]
        public InteractionMode Mode
        {
            get { return _mode; }
            set
            {
                if (_mode == value) return;
                _mode = value;
                CancelDrawing();
                UpdateCursor();
                Invalidate();
            }
        }

        /// <summary>滚轮每一格的缩放倍率，默认 1.15。</summary>
        [Category("Behavior"), DefaultValue(1.15f)]
        public float ZoomStep
        {
            get { return _zoomStep; }
            set { _zoomStep = value > 1.01f ? value : 1.01f; }
        }

        /// <summary>当前缩放倍率（1 = 图像像素与屏幕像素 1:1）。</summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float Zoom
        {
            get { return _viewport.Zoom; }
            set { SetZoom(value); }
        }

        /// <summary>控件是否拥有图像所有权（切换图像或释放控件时自动 Dispose 旧图像）。</summary>
        [Category("Behavior"), DefaultValue(true)]
        public bool OwnsImage
        {
            get { return _ownsImage; }
            set { _ownsImage = value; }
        }

        /// <summary>控件尺寸变化时是否自动重新适应窗口。</summary>
        [Category("Behavior"), DefaultValue(false)]
        public bool AutoFitOnResize
        {
            get { return _autoFitOnResize; }
            set { _autoFitOnResize = value; }
        }

        /// <summary>新建图形的默认线条颜色。</summary>
        [Category("Appearance")]
        public Color NewShapeColor
        {
            get { return _newShapeColor; }
            set { _newShapeColor = value; }
        }

        /// <summary>新建图形的默认线宽（屏幕像素）。</summary>
        [Category("Appearance"), DefaultValue(2f)]
        public float NewShapeLineWidth
        {
            get { return _newShapeLineWidth; }
            set { _newShapeLineWidth = value; }
        }

        /// <summary>是否显示跟随鼠标的辅助十字线（可用右键菜单切换）。</summary>
        [Category("Behavior"), DefaultValue(false)]
        [Description("是否显示跟随鼠标的辅助十字线。")]
        public bool ShowCrosshair
        {
            get { return _renderOptions.ShowCrosshair; }
            set
            {
                if (_renderOptions.ShowCrosshair == value) return;
                _renderOptions.ShowCrosshair = value;
                Invalidate();
            }
        }

        /// <summary>
        /// 是否在右下角显示 HUD 面板：鼠标所在的图像坐标，
        /// 以及该点的像素值（灰度图显示 Gray，彩色图显示 Color 与 RGB）。
        /// </summary>
        [Category("Behavior"), DefaultValue(true)]
        [Description("在右下角显示鼠标所在的图像坐标与像素值。")]
        public bool ShowHud
        {
            get { return _renderOptions.ShowHud; }
            set
            {
                if (_renderOptions.ShowHud == value) return;
                _renderOptions.ShowHud = value;
                Invalidate();
            }
        }

        /// <summary>
        /// 控件的右键菜单，外部可追加自己的菜单项。
        /// 内置项：保存图像、保存视图截图、自适应图像、显示辅助十字线。
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public ContextMenuStrip ViewerContextMenu
        {
            get { return _contextMenu; }
        }

        /// <summary>鼠标当前所在的图像坐标。</summary>
        [Browsable(false)]
        public PointF MouseImagePosition
        {
            get { return _mouseImagePoint; }
        }

        /// <summary>
        /// 是否抑制右键菜单（默认 <c>false</c>）。
        /// <para>
        /// 宿主在"正在绘制 / 编辑几何"期间置 <c>true</c>：那段时间右键是「取消 / 完成」的语义，
        /// 再弹菜单既干扰操作也容易误点。退出这些状态必须置回 <c>false</c> ——
        /// 漏复位不会报错、不会崩，只是右键菜单从此静默消失，属于最难查的一类问题。
        /// </para>
        /// </summary>
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool SuppressContextMenu { get; set; }

        /// <summary>图像尺寸（无图像时为 Empty）。</summary>
        [Browsable(false)]
        public Size ImageSize
        {
            get { return _viewport.ImageSize; }
        }

        #endregion

        #region 图像操作

        /// <summary>设置显示的图像。</summary>
        /// <param name="image">位图；传 null 表示清空显示。</param>
        /// <param name="autoFit">是否自动缩放至完整可见，默认 true。</param>
        public void SetImage(Bitmap image, bool autoFit = true)
        {
            if (ReferenceEquals(_image, image)) return;

            // 2D 图一到，3D 点云模式就让位（互斥）
            if (_cloudMode) ClearPointCloud();

            // 灰度判定必须在转换前做：转换后一律是 24bpp，就无法再区分了
            bool isGray = IsGrayImage(image);
            Bitmap displayImage = ToDisplayBitmap(image);
            bool producedCopy = (image != null) && !ReferenceEquals(displayImage, image);

            if (_ownsDisplayImage && _image != null)
                _image.Dispose();

            // 已生成副本时，原图对控件不再有用；仅在拥有所有权的前提下释放它
            if (_ownsImage && producedCopy)
                image.Dispose();

            _image = displayImage;
            _ownsDisplayImage = _ownsImage || producedCopy;
            _imageIsGray = isGray;
            _mousePixelColor = null;
            _imageLayerDirty = true;

            // 判断是否处于「连续换图」：连续运行 / 实时预览时每帧都会进来，
            // 这时重建金字塔纯属浪费（见 LiveStreamThresholdMs 的实测数据）。
            long stamp = Stopwatch.GetTimestamp();
            double sinceMs = _lastImageStamp == 0
                ? double.MaxValue
                : (stamp - _lastImageStamp) * 1000.0 / Stopwatch.Frequency;
            _liveStream = sinceMs < LiveStreamThresholdMs;
            _lastImageStamp = stamp;

            // 新图：绘制计数归零（决定要不要建金字塔，见 _paintsOfCurrentImage 的说明）
            _paintsOfCurrentImage = 0;

            // 立即释放旧金字塔，避免换图后旧层继续占着内存
            ClearPyramid();
            _pyramidDirty = true;
            _viewport.ImageSize = (displayImage != null) ? displayImage.Size : Size.Empty;

            if (displayImage != null && autoFit)
                _viewport.Fit(ClientSize);

            Invalidate();
            OnImageChanged();
        }

        /// <summary>
        /// 直接把 OpenCV 的 <c>Mat</c> 像素数据显示到控件上（内部完成 BGR→RGB 等转换并深拷贝）。
        /// </summary>
        /// <param name="data">Mat 首像素指针（OpenCvSharp 为 <c>mat.Data</c>）。</param>
        /// <param name="width">图像宽度（<c>mat.Cols</c>）。</param>
        /// <param name="height">图像高度（<c>mat.Rows</c>）。</param>
        /// <param name="stride">每行字节数（<c>(int)mat.Step()</c>）。传 0 表示无行填充。</param>
        /// <param name="layout">像素排布，OpenCV 彩色图默认 BGR。</param>
        /// <param name="autoFit">是否自动缩放至完整可见。</param>
        public void SetCvImage(IntPtr data, int width, int height, int stride, CvPixelLayout layout, bool autoFit = true)
        {
            Bitmap bmp = CvInterop.ToBitmap(data, width, height, stride, layout);
            SetImage(bmp, autoFit);
        }

        /// <summary>清空图像但保留图形。</summary>
        public void ClearImage()
        {
            SetImage(null);
        }

        // =====================================================================
        //  点云显示（3D，与 2D 图层共用本控件、互斥显示）
        // =====================================================================

        /// <summary>
        /// 显示点云（进入 3D 模式：内部创建 OpenGL 子面板，支持拖动旋转/滚轮缩放/双击复位）。
        /// <para>传 null 或空云 = 清空点云并回到 2D 空白。仅持引用，不接管释放。</para>
        /// </summary>
        public void SetPointCloud(PointCloud.PointCloudData cloud)
        {
            if (cloud == null || cloud.Count <= 0)
            {
                ClearPointCloud();
                return;
            }

            if (_glView == null)
            {
                _glView = new PointCloud.GlPointView { Dock = DockStyle.Fill };
                Controls.Add(_glView);
            }

            _cloudMode = true;
            _cloud = cloud;
            _glView.Visible = true;
            _glView.SetCloud(cloud);
            _glView.BringToFront();
        }

        /// <summary>退出 3D 点云模式（回到 2D）。</summary>
        public void ClearPointCloud()
        {
            _cloudMode = false;
            _cloud = null;
            if (_glView != null)
            {
                _glView.SetCloud(null);
                _glView.Visible = false;
            }
            Invalidate();
        }

        /// <summary>当前是否处于 3D 点云显示模式。</summary>
        [Browsable(false)]
        public bool IsPointCloudMode
        {
            get { return _cloudMode; }
        }

        /// <summary>把当前图像导出为 OpenCV 可直接使用的连续像素数组。</summary>
        public byte[] GetImageBytes(CvPixelLayout layout = CvPixelLayout.Bgr24)
        {
            if (_image == null) return null;
            return CvInterop.ToBytes(_image, layout);
        }

        /// <summary>读取图像坐标处的像素颜色；坐标越界时返回 null。</summary>
        public Color? GetPixelColor(PointF imagePoint)
        {
            if (_image == null) return null;
            int x = (int)Math.Floor(imagePoint.X);
            int y = (int)Math.Floor(imagePoint.Y);
            if (x < 0 || y < 0 || x >= _image.Width || y >= _image.Height) return null;
            try
            {
                return _image.GetPixel(x, y);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region 视图操作

        /// <summary>缩放至完整可见并居中。</summary>
        public void FitToWindow()
        {
            _viewport.Fit(ClientSize);
            InvalidateView();
            OnZoomChanged();
        }

        /// <summary>设置缩放倍率，以控件中心为锚点。</summary>
        public void SetZoom(float zoom)
        {
            PointF center = new PointF(ClientSize.Width * 0.5f, ClientSize.Height * 0.5f);
            _viewport.SetZoomAt(zoom, center);
            InvalidateView();
            OnZoomChanged();
        }

        /// <summary>放大一档（以控件中心为锚点）。</summary>
        public void ZoomIn()
        {
            _viewport.ZoomAt(GetCenterControlPoint(), _zoomStep);
            InvalidateView();
            OnZoomChanged();
        }

        /// <summary>缩小一档（以控件中心为锚点）。</summary>
        public void ZoomOut()
        {
            _viewport.ZoomAt(GetCenterControlPoint(), 1f / _zoomStep);
            InvalidateView();
            OnZoomChanged();
        }

        /// <summary>恢复 1:1 显示。</summary>
        public void ZoomActual()
        {
            SetZoom(1f);
        }

        /// <summary>把指定图像坐标点滚动到控件中心。</summary>
        public void CenterOn(PointF imagePoint)
        {
            _viewport.CenterOn(imagePoint, ClientSize);
            InvalidateView();
        }

        /// <summary>图像坐标 → 控件坐标。</summary>
        public PointF ImageToControl(PointF p)
        {
            return _viewport.ImageToControl(p);
        }

        /// <summary>控件坐标 → 图像坐标。</summary>
        public PointF ControlToImage(PointF p)
        {
            return _viewport.ControlToImage(p);
        }

        private PointF GetCenterControlPoint()
        {
            return new PointF(ClientSize.Width * 0.5f, ClientSize.Height * 0.5f);
        }

        #endregion

        #region 图形操作

        /// <summary>选中指定图形（同时取消其它图形的选中）。</summary>
        public void SelectShape(IGeometryShape shape)
        {
            bool changed = false;
            foreach (IGeometryShape s in _shapes)
            {
                if (s == null) continue;
                bool shouldSelect = ReferenceEquals(s, shape);
                if (s.IsSelected != shouldSelect)
                {
                    s.IsSelected = shouldSelect;
                    changed = true;
                }
            }
            if (changed)
            {
                OnSelectionChanged();
                Invalidate();
            }
        }

        /// <summary>删除所有选中图形，返回删除数量。</summary>
        public int DeleteSelected()
        {
            int removed = _shapes.RemoveSelected();
            if (removed > 0)
                OnSelectionChanged();
            Invalidate();
            return removed;
        }

        /// <summary>删除全部图形。</summary>
        public void ClearShapes()
        {
            _shapes.Clear();
            _previewShape = null;
            OnSelectionChanged();
            Invalidate();
        }

        /// <summary>获取当前选中的第一个图形，无选中则返回 null。</summary>
        public IGeometryShape GetSelectedShape()
        {
            foreach (IGeometryShape s in _shapes)
            {
                if (s != null && s.IsSelected) return s;
            }
            return null;
        }

        /// <summary>
        /// 把当前所有图形导出为 OpenCV 参数文本（每行一个图形），
        /// 便于直接写入配置或喂给 C++ 侧解析。
        /// </summary>
        public string ExportShapesToCvText()
        {
            var lines = new List<string>();
            foreach (IGeometryShape s in _shapes)
            {
                if (s == null || !s.Visible) continue;
                lines.Add(CvInterop.ToCvParameterString(s));
            }
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        #endregion

        #region 鼠标与键盘

        /// <summary>滚轮缩放：以鼠标位置为锚点。</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);

            float step = _zoomStep;
            if ((Control.ModifierKeys & Keys.Control) != 0)
                step = _zoomStep * _zoomStep; // Ctrl 加速

            float factor = (e.Delta > 0) ? step : (1f / step);
            _viewport.ZoomAt(e.Location, factor);

            InvalidateView();
            OnZoomChanged();
        }

        /// <summary>鼠标按下：根据模式进入平移 / 绘制 / 选择拖拽。</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            PointF imagePt = _viewport.ControlToImage(e.Location);

            bool wantPan = (e.Button == MouseButtons.Middle)
                           || (_mode == InteractionMode.Pan)
                           || (_spaceHeld && e.Button == MouseButtons.Left);

            if (wantPan)
            {
                StartPan(e.Location);
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                if (_drawing)
                {
                    CancelDrawing();
                    return;
                }
                // 右键按下先进入平移；抬起时若未发生拖动则弹出右键菜单
                _rightButtonDown = true;
                _rightDragged = false;
                StartPan(e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left) return;

            if (IsDrawingMode(_mode))
                StartDrawing(imagePt, e.Location);
            else
                StartSelectOrDrag(imagePt, e.Location);
        }

        /// <summary>鼠标移动。</summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            _mouseInside = true;
            _mouseControlPoint = e.Location;
            PointF imagePt = _viewport.ControlToImage(e.Location);
            _mouseImagePoint = imagePt;

            if (_panning)
            {
                if (Math.Abs(e.X - _panStartScreen.X) > MenuDragThresholdPx ||
                    Math.Abs(e.Y - _panStartScreen.Y) > MenuDragThresholdPx)
                {
                    _rightDragged = true;
                }

                _viewport.OffsetX = _panStartOffsetX + (e.X - _panStartScreen.X);
                _viewport.OffsetY = _panStartOffsetY + (e.Y - _panStartScreen.Y);
                InvalidateView();
                return;
            }

            if (_drawing)
            {
                UpdateDrawing(imagePt);
                Invalidate();
                return;
            }

            if (_draggingShape && _activeShape != null)
            {
                // Shift 吸附 15°：读修饰键的职责只放在控件里（RotatedRectShape 不引用 WinForms），
                // 且必须在 SetHandlePosition 之前设好，否则本帧的旋转不会吸附。
                RotatedRectShape draggingRr = _activeShape as RotatedRectShape;
                if (draggingRr != null)
                    draggingRr.SnapStepDeg = ((ModifierKeys & Keys.Shift) == Keys.Shift) ? 15f : 0f;

                if (_activeHandleIndex >= 0)
                {
                    _activeShape.SetHandlePosition(_activeHandleIndex, imagePt);
                }
                else
                {
                    float dx = imagePt.X - _lastImagePoint.X;
                    float dy = imagePt.Y - _lastImagePoint.Y;
                    _activeShape.Translate(dx, dy);
                    _lastImagePoint = imagePt;
                }
                _shapes.NotifyChanged();
                return;
            }

            UpdateCursor();

            // 缓存当前像素值，HUD 与事件共用，避免每次重绘都重新取色
            _mousePixelColor = GetPixelColor(imagePt);
            OnMouseImageMove(imagePt, e.Location);

            if (_renderOptions.ShowCrosshair || _renderOptions.ShowHud)
                Invalidate();
        }

        /// <summary>鼠标抬起：结束当前交互。</summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_panning)
            {
                bool wasRightButton = _rightButtonDown;
                _panning = false;
                _rightButtonDown = false;
                Capture = false;
                UpdateCursor();

                // 右键点击（未拖动）弹出菜单；右键拖动属于平移，不弹菜单
                if (wasRightButton && !_rightDragged)
                    ShowContextMenu(e.Location);

                _rightDragged = false;
                return;
            }

            if (_drawing)
            {
                FinishDrawing(e.Location);
                Capture = false;
                return;
            }

            if (_draggingShape)
            {
                IGeometryShape committed = _activeShape;

                _draggingShape = false;
                _activeShape = null;
                _activeHandleIndex = -1;
                Capture = false;
                Invalidate();

                // 松手 = 用户调整完了：此时才让宿主落盘一次（拖动过程只刷预览）。
                // 顺手把吸附步长清 0，避免后续程序化 SetHandlePosition 被上一次的 Shift 状态影响。
                if (committed != null)
                {
                    RotatedRectShape committedRr = committed as RotatedRectShape;
                    if (committedRr != null) committedRr.SnapStepDeg = 0f;

                    OnShapeCommitted(committed);
                }
            }
        }

        /// <summary>鼠标离开控件。</summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseInside = false;
            if (_renderOptions.ShowCrosshair || _renderOptions.ShowHud)
                Invalidate();
        }

        /// <summary>键盘快捷键：空格平移、Delete 删除、Esc 取消、+/- 缩放、0 适应、1 原始尺寸。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Space)
            {
                _spaceHeld = true;
                Cursor = Cursors.SizeAll;
                e.Handled = true;
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Delete:
                    DeleteSelected();
                    e.Handled = true;
                    break;

                case Keys.Escape:
                    CancelDrawing();
                    _shapes.ClearSelection();
                    OnSelectionChanged();
                    Invalidate();
                    e.Handled = true;
                    break;

                case Keys.Add:
                case Keys.Oemplus:
                    ZoomIn();
                    e.Handled = true;
                    break;

                case Keys.Subtract:
                case Keys.OemMinus:
                    ZoomOut();
                    e.Handled = true;
                    break;

                case Keys.D0:
                case Keys.NumPad0:
                    FitToWindow();
                    e.Handled = true;
                    break;

                case Keys.D1:
                case Keys.NumPad1:
                    ZoomActual();
                    e.Handled = true;
                    break;
            }
        }

        /// <summary>松开空格退出平移准备状态。</summary>
        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space)
            {
                _spaceHeld = false;
                UpdateCursor();
                e.Handled = true;
            }
        }

        /// <summary>失去焦点时清理临时状态。</summary>
        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _spaceHeld = false;
        }

        /// <summary>尺寸变化。</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _imageLayerDirty = true; // 尺寸变化后缓存层需按新尺寸重建
            if (_autoFitOnResize && _image != null)
                _viewport.Fit(ClientSize);
        }

        #endregion

        #region 交互内部实现

        private void StartPan(Point screenPt)
        {
            _panning = true;
            _panStartScreen = screenPt;
            _panStartOffsetX = _viewport.OffsetX;
            _panStartOffsetY = _viewport.OffsetY;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }

        private void StartDrawing(PointF imagePt, Point screenPt)
        {
            _drawing = true;
            _drawStartImage = imagePt;
            _drawStartScreen = screenPt;
            _previewShape = null;
            Capture = true;
        }

        private void UpdateDrawing(PointF imagePt)
        {
            switch (_mode)
            {
                case InteractionMode.DrawCircle:
                {
                    float dx = imagePt.X - _drawStartImage.X;
                    float dy = imagePt.Y - _drawStartImage.Y;
                    float r = (float)Math.Sqrt(dx * dx + dy * dy);
                    _previewShape = new CircleShape(_drawStartImage, r);
                    break;
                }

                case InteractionMode.DrawRectangle:
                {
                    float x = Math.Min(_drawStartImage.X, imagePt.X);
                    float y = Math.Min(_drawStartImage.Y, imagePt.Y);
                    float w = Math.Abs(imagePt.X - _drawStartImage.X);
                    float h = Math.Abs(imagePt.Y - _drawStartImage.Y);
                    _previewShape = new RectangleShape(x, y, w, h);
                    break;
                }

                case InteractionMode.DrawRotatedRect:
                {
                    float w = Math.Abs(imagePt.X - _drawStartImage.X);
                    float h = Math.Abs(imagePt.Y - _drawStartImage.Y);
                    PointF center = new PointF(
                        (_drawStartImage.X + imagePt.X) * 0.5f,
                        (_drawStartImage.Y + imagePt.Y) * 0.5f);
                    _previewShape = new RotatedRectShape(center, w, h, 0f);
                    break;
                }

                case InteractionMode.DrawRing:
                {
                    float dx = imagePt.X - _drawStartImage.X;
                    float dy = imagePt.Y - _drawStartImage.Y;
                    float outer = (float)Math.Sqrt(dx * dx + dy * dy);
                    // 内半径默认取外半径的一半，随后可用内圆控制点调整
                    _previewShape = new RingShape(_drawStartImage, outer, outer * 0.5f);
                    break;
                }

                case InteractionMode.DrawLine:
                {
                    // 线段：起点固定为按下点，终点跟随当前鼠标（图像坐标）
                    _previewShape = new LineSegmentShape(_drawStartImage, imagePt);
                    break;
                }

                default:
                    return;
            }

            if (_previewShape != null)
            {
                _previewShape.StrokeColor = _newShapeColor;
                _previewShape.LineWidth = _newShapeLineWidth;
                _previewShape.IsSelected = true;
            }
        }

        private void FinishDrawing(Point screenPt)
        {
            bool wasDrawing = _drawing;
            _drawing = false;

            IGeometryShape created = _previewShape;
            _previewShape = null;

            if (!wasDrawing || created == null)
            {
                Invalidate();
                return;
            }

            // 记住本次画的是哪种图形：下面触发的事件里宿主可能改 Mode（矩形路径会恢复原模式）
            bool wasRotatedRect = (_mode == InteractionMode.DrawRotatedRect);

            // 过滤误点击产生的微小图形
            float dx = screenPt.X - _drawStartScreen.X;
            float dy = screenPt.Y - _drawStartScreen.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);

            if (dist >= MinDrawDistancePx)
            {
                created.IsSelected = true;
                _shapes.Add(created);
                OnShapeCreated(created);

                // 旋转矩形画完必须切回选择态：否则用户拖不到刚加的 8 个手柄（"画完没法转"）。
                // 只对 DrawRotatedRect 生效 —— 其它绘制模式仍由宿主自己恢复原模式，行为一字不改。
                if (wasRotatedRect && _mode == InteractionMode.DrawRotatedRect)
                    Mode = InteractionMode.Select;
            }

            Invalidate();
        }

        private void CancelDrawing()
        {
            if (!_drawing) return;
            _drawing = false;
            _previewShape = null;
            Capture = false;
            Invalidate();
        }

        private void StartSelectOrDrag(PointF imagePt, Point screenPt)
        {
            float handleTol = Math.Max(_renderOptions.HandleSize * 0.75f, 5f);

            // 1) 优先命中已选中图形的控制点
            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                IGeometryShape s = _shapes[i];
                if (s == null || !s.Visible || !s.IsEditable || !s.IsSelected) continue;

                IList<ShapeHandle> handles = s.GetHandles();
                for (int k = 0; k < handles.Count; k++)
                {
                    PointF hp = _viewport.ImageToControl(handles[k].Position);
                    if (Math.Abs(hp.X - screenPt.X) <= handleTol && Math.Abs(hp.Y - screenPt.Y) <= handleTol)
                    {
                        _activeShape = s;
                        _activeHandleIndex = handles[k].Index;
                        _draggingShape = true;
                        Capture = true;
                        return;
                    }
                }
            }

            // 2) 命中图形主体：后绘制的优先（视觉上在顶层）
            float tolImage = _viewport.UnscaleLength(HitTolerancePx);
            for (int i = _shapes.Count - 1; i >= 0; i--)
            {
                IGeometryShape s = _shapes[i];
                if (s == null || !s.Visible || !s.IsEditable) continue;

                if (s.HitTest(imagePt, tolImage))
                {
                    SelectShape(s);
                    _activeShape = s;
                    _activeHandleIndex = -1;
                    _draggingShape = true;
                    _lastImagePoint = imagePt;
                    Capture = true;
                    return;
                }
            }

            // 3) 点在空白处：取消选择
            if (_shapes.GetSelected().Count > 0)
            {
                _shapes.ClearSelection();
                OnSelectionChanged();
                Invalidate();
            }
        }

        /// <summary>
        /// 判断模式是否属于「拖拽绘制新图形」类。
        /// 新增图形类型时只需在此处登记，鼠标按下分发（<see cref="OnMouseDown"/>）
        /// 与光标切换（<see cref="UpdateCursor"/>）会自动生效，避免漏改其中一处。
        /// </summary>
        private static bool IsDrawingMode(InteractionMode mode)
        {
            return mode == InteractionMode.DrawCircle
                || mode == InteractionMode.DrawRectangle
                || mode == InteractionMode.DrawRotatedRect
                || mode == InteractionMode.DrawRing
                || mode == InteractionMode.DrawLine;
        }

        private void UpdateCursor()
        {
            if (_panning || _spaceHeld || _mode == InteractionMode.Pan)
            {
                Cursor = Cursors.SizeAll;
                return;
            }

            if (IsDrawingMode(_mode))
            {
                Cursor = Cursors.Cross;
                return;
            }

            Cursor = Cursors.Default;
        }

        #endregion

        #region 右键菜单与图像导出

        /// <summary>构建内置右键菜单。外部可通过 <see cref="ViewerContextMenu"/> 追加自定义项。</summary>
        private void BuildContextMenu()
        {
            _contextMenu = new ContextMenuStrip();

            // Name 必须显式指定：语言设施按 "ImageViewer.<菜单项 Name>" 在对照表里
            // 查译文（见 VisionPlatform.Core.Lang.ApplySelfDrawnMenu），
            // 默认值是空字符串会导致查不到而跳过翻译。
            _menuSaveImage = new ToolStripMenuItem("保存图像(&S)");
            _menuSaveImage.Name = "_menuSaveImage";
            _menuSaveImage.ToolTipText = "把当前图像（不含图形叠加）保存为文件";
            _menuSaveImage.Click += (s, e) => SaveImageToFile();

            _menuSaveSnapshot = new ToolStripMenuItem("保存视图截图(&V)");
            _menuSaveSnapshot.Name = "_menuSaveSnapshot";
            _menuSaveSnapshot.ToolTipText = "把当前所见画面（含图形叠加、网格）保存为文件";
            _menuSaveSnapshot.Click += (s, e) => SaveSnapshotToFile();

            _menuFitImage = new ToolStripMenuItem("自适应图像(&F)");
            _menuFitImage.Name = "_menuFitImage";
            _menuFitImage.ToolTipText = "缩放至完整可见并居中";
            _menuFitImage.Click += (s, e) => FitToWindow();

            _menuCrosshair = new ToolStripMenuItem("显示辅助十字线(&C)");
            _menuCrosshair.Name = "_menuCrosshair";
            _menuCrosshair.CheckOnClick = true;
            _menuCrosshair.Click += (s, e) => ShowCrosshair = _menuCrosshair.Checked;

            _contextMenu.Items.Add(_menuSaveImage);
            _contextMenu.Items.Add(_menuSaveSnapshot);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_menuFitImage);
            _contextMenu.Items.Add(_menuCrosshair);

            _contextMenu.Opening += (s, e) =>
            {
                bool hasImage = (_image != null);
                _menuSaveImage.Enabled = hasImage;
                _menuSaveSnapshot.Enabled = hasImage;
                _menuFitImage.Enabled = hasImage;
                _menuCrosshair.Checked = _renderOptions.ShowCrosshair;
            };
        }

        /// <summary>在指定位置弹出右键菜单。</summary>
        private void ShowContextMenu(Point location)
        {
            // 宿主主动抑制右键菜单：ROI 绘制 / 编辑期间右键等于「取消 / 完成」，
            // 此时再弹一个与当前操作无关的菜单纯属干扰（且会挡住「右键取消」的直觉）。
            // 刻意做成属性而不是把 ViewerContextMenu 置 null —— 后者只有 getter，改不了。
            if (SuppressContextMenu) return;

            if (_contextMenu == null) return;
            _contextMenu.Show(this, location);
        }

        /// <summary>
        /// 弹出「另存为」对话框，把当前图像（不含图形叠加）保存为文件。
        /// </summary>
        /// <returns>保存成功返回 true；用户取消或保存失败返回 false。</returns>
        public bool SaveImageToFile()
        {
            if (_image == null) return false;

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "保存图像";
                dlg.Filter = "PNG 图像|*.png|JPEG 图像|*.jpg;*.jpeg|BMP 图像|*.bmp|TIFF 图像|*.tif;*.tiff";
                dlg.DefaultExt = "png";
                dlg.FileName = "image.png";
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;

                try
                {
                    ImageFormat format = GetImageFormat(dlg.FileName);

                    // JPEG 不支持索引色（灰度 8bpp），必要时先转成 24bpp
                    Bitmap temp = null;
                    try
                    {
                        Bitmap toSave = _image;
                        if (NeedsRgbConversion(_image, format))
                        {
                            temp = ConvertTo24Bpp(_image);
                            toSave = temp;
                        }
                        toSave.Save(dlg.FileName, format);
                    }
                    finally
                    {
                        if (temp != null) temp.Dispose();
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存图像失败：" + ex.Message, "保存图像",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }
        }

        /// <summary>
        /// 截取当前控件可见区域（含图形叠加、网格、背景）并保存为文件。
        /// </summary>
        public bool SaveSnapshotToFile()
        {
            Bitmap snapshot = CreateSnapshot();
            if (snapshot == null) return false;

            try
            {
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Title = "保存视图截图";
                    dlg.Filter = "PNG 图像|*.png|JPEG 图像|*.jpg;*.jpeg|BMP 图像|*.bmp";
                    dlg.DefaultExt = "png";
                    dlg.FileName = "snapshot.png";
                    if (dlg.ShowDialog(this) != DialogResult.OK) return false;

                    snapshot.Save(dlg.FileName, GetImageFormat(dlg.FileName));
                    return true;
                }
            }
            finally
            {
                snapshot.Dispose();
            }
        }

        /// <summary>生成当前控件可见区域的位图快照（含背景、图像、网格、图形）。</summary>
        public Bitmap CreateSnapshot()
        {
            int w = Math.Max(ClientSize.Width, 1);
            int h = Math.Max(ClientSize.Height, 1);

            var bmp = new Bitmap(w, h);
            try
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                return bmp;
            }
            catch (Exception)
            {
                // DrawToBitmap 在个别宿主环境下不可用，退回手动渲染
                bmp.Dispose();
                var fallback = new Bitmap(w, h);
                using (Graphics g = Graphics.FromImage(fallback))
                {
                    var args = new PaintEventArgs(g, new Rectangle(0, 0, w, h));
                    OnPaintBackground(args);
                    OnPaint(args);
                }
                return fallback;
            }
        }

        private static ImageFormat GetImageFormat(string path)
        {
            string ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext))
            {
                switch (ext.ToLowerInvariant())
                {
                    case ".jpg":
                    case ".jpeg": return ImageFormat.Jpeg;
                    case ".bmp": return ImageFormat.Bmp;
                    case ".tif":
                    case ".tiff": return ImageFormat.Tiff;
                }
            }
            return ImageFormat.Png;
        }

        private static bool NeedsRgbConversion(Bitmap src, ImageFormat format)
        {
            return (format == ImageFormat.Jpeg) &&
                   ((src.PixelFormat & PixelFormat.Indexed) != 0);
        }

        private static Bitmap ConvertTo24Bpp(Bitmap src)
        {
            var converted = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(converted))
            {
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return converted;
        }

        #endregion

        #region 绘制

        /// <summary>
        /// 确保静态内容缓存层可用（背景 + 图像 + 像素网格）。
        /// 仅在图像、缩放/平移、控件尺寸变化后重建；
        /// 鼠标移动、拖拽图形这些不改变视图的操作可直接复用缓存，
        /// 省掉每帧一次的大图缩放绘制。
        /// </summary>
        private void EnsureImageLayer()
        {
            int w = Math.Max(ClientSize.Width, 1);
            int h = Math.Max(ClientSize.Height, 1);

            if (_imageLayer == null || _imageLayer.Width != w || _imageLayer.Height != h)
            {
                if (_imageLayer != null)
                    _imageLayer.Dispose();
                _imageLayer = new Bitmap(w, h);
                _imageLayerDirty = true;
            }

            if (!_imageLayerDirty) return;

            long t0 = Stopwatch.GetTimestamp();
            using (Graphics g = Graphics.FromImage(_imageLayer))
            {
                g.Clear(_renderOptions.BackdropColor);
                DrawImage(g);
                DrawPixelGrid(g);
            }
            double layerMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            LastLayerRebuildMs = layerMs;
            LayerRebuildCount++;
            LayerRebuildTotalMs += layerMs;

            _imageLayerDirty = false;
        }

        /// <summary>
        /// 累计重绘次数（诊断用）。连续运行时配合 <c>LastLayerRebuildMs</c>
        /// 就能算出"界面这块到底占了多少 CPU"。
        /// </summary>
        public long PaintCount { get; private set; }

        /// <summary>
        /// 最近一次「重建缓存层」的耗时（毫秒）；还没重建过时为 0（命中缓存时不更新）。
        /// <para>连续运行时 CPU 高最常见的原因就是这里：每来一帧都要把整幅原图缩放到视图尺寸。
        /// 现场排查可直接看这个数（配合 <c>VA_UI_STATS=1</c> 的界面统计日志）。</para>
        /// </summary>
        public double LastLayerRebuildMs { get; private set; }

        /// <summary>累计重建缓存层的次数（诊断用）。</summary>
        public long LayerRebuildCount { get; private set; }

        /// <summary>累计重建缓存层的总耗时（毫秒，诊断用）。用区间增量算平均才是真实值。</summary>
        public double LayerRebuildTotalMs { get; private set; }

        /// <summary>
        /// 强制重建静态内容缓存层。
        /// 直接修改 <see cref="Options"/> 中的背景色、网格、插值模式后需要调用它，
        /// 只调用 <see cref="Control.Invalidate()"/> 不会重建缓存。
        /// </summary>
        public void InvalidateImageLayer()
        {
            _imageLayerDirty = true;
            Invalidate();
        }

        /// <summary>视图（缩放 / 平移）发生变化：缓存层失效并请求重绘。</summary>
        private void InvalidateView()
        {
            _imageLayerDirty = true;
            Invalidate();
        }

        /// <summary>
        /// 背景已绘制在缓存层 <c>_imageLayer</c> 中（不透明），
        /// 此处不再擦除，省掉一次全屏填充。
        /// </summary>
        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        /// <summary>
        /// 主绘制流程：缓存层贴底 → 图形 → 控制点 → 十字线 → HUD。
        /// 图像与像素网格取自缓存，只有图形与鼠标相关的动态内容每帧重绘。
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            PaintCount++;
            Graphics g = e.Graphics;

            EnsureImageLayer();
            g.DrawImage(_imageLayer, 0, 0);

            // 标签避让区 = 客户区：靠右/靠下的框，其标签往内挪，绝不画到画面外
            _renderOptions.LabelClipBounds =
                new RectangleF(0f, 0f, ClientSize.Width, ClientSize.Height);

            DrawShapes(g);
            DrawHandles(g);
            DrawCrosshair(g);
            DrawHud(g);
            DrawEmptyHint(g);

            base.OnPaint(e);
        }

        /// <summary>绘制图像。只渲染当前可见区域，因此放大到几十倍依然流畅。</summary>
        private void DrawImage(Graphics g)
        {
            if (_image == null) return;

            RectangleF visible = _viewport.GetVisibleImageRect(ClientSize);
            float x0 = Math.Max(0f, visible.Left);
            float y0 = Math.Max(0f, visible.Top);
            float x1 = Math.Min(_image.Width, visible.Right);
            float y1 = Math.Min(_image.Height, visible.Bottom);

            if (x1 > x0 && y1 > y0)
            {
                float srcW = x1 - x0;
                float srcH = y1 - y0;

                // 可见源区域过大时改用金字塔层：层的分辨率更贴近屏幕需求，
                // 采样量直接下降一个数量级。缩放只是换层，不需要重新生成。
                // 【只在"同一张图被重画"时才建】第一遍直接按屏幕尺寸采样：
                // 流程慢的时候一帧只画一次，建层是纯浪费（现场实测 620~833ms/帧）。
                _paintsOfCurrentImage++;
                bool allowPyramid = _paintsOfCurrentImage > 1;

                Bitmap source = _image;
                float levelScale = 1f;
                if (allowPyramid && (long)(srcW * srcH) > LargeSourcePixelThreshold)
                {
                    // 只建当前缩放会用到的层数（fit 视图通常 2 层，而不是固定的 12 层）
                    int needed = NeededPyramidLevels(_viewport.Zoom);
                    EnsurePyramid(needed);
                    Bitmap level = SelectPyramidLevel(_viewport.Zoom, out levelScale);
                    if (level != null)
                        source = level;
                    else
                        levelScale = 1f;
                }

                // 源矩形按层缩放换算，目标矩形仍是屏幕坐标
                var src = new RectangleF(x0 * levelScale, y0 * levelScale,
                                         srcW * levelScale, srcH * levelScale);
                RectangleF dst = _viewport.ImageToControl(new RectangleF(x0, y0, srcW, srcH));

                InterpolationMode oldInterp = g.InterpolationMode;
                PixelOffsetMode oldPixelOffset = g.PixelOffsetMode;
                SmoothingMode oldSmoothing = g.SmoothingMode;

                InterpolationMode mode = (_viewport.Zoom >= 1f)
                    ? _renderOptions.ZoomInInterpolation
                    : _renderOptions.ZoomOutInterpolation;

                // 源区域过大时双三次要遍历大量像素，自动降级为双线性
                if (mode == InterpolationMode.HighQualityBicubic &&
                    (long)(src.Width * src.Height) > LargeSourcePixelThreshold)
                {
                    mode = InterpolationMode.HighQualityBilinear;
                }

                // 连续流：HighQuality* 在这条路上是纯软件光栅的高成本分支
                // （5MP→800×600 实测 HQ 双线性 31ms vs 普通双线性 14ms）。
                // 实时画面只求"看得清"，不值得为插值质量付一倍 CPU。
                if (IsLiveStreaming &&
                    (mode == InterpolationMode.HighQualityBilinear
                     || mode == InterpolationMode.HighQualityBicubic))
                {
                    mode = InterpolationMode.Bilinear;
                }

                g.InterpolationMode = mode;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.SmoothingMode = SmoothingMode.None;

                // 【下采样走整数矩形】fit 视图的 dst 是 zoom 算出来的浮点矩形（如 0.5/783.2），
                // GDI+ 遇到非整数目标矩形会退到通用仿射变换路径 —— 同一张 5MP 图，
                // 实测整数矩形 14ms、带小数的矩形 32ms（2.3 倍）。
                // 缩小时（zoom<1）半像素误差在屏幕上看不出来，取整换 2 倍速度；
                // 放大检查时保留浮点精度，平移仍是亚像素顺滑。
                if (_viewport.Zoom < 1f)
                {
                    g.DrawImage(source, Rectangle.Round(dst), Rectangle.Round(src), GraphicsUnit.Pixel);
                }
                else
                {
                    g.DrawImage(source, dst, src, GraphicsUnit.Pixel);
                }

                g.InterpolationMode = oldInterp;
                g.PixelOffsetMode = oldPixelOffset;
                g.SmoothingMode = oldSmoothing;
            }

            if (_renderOptions.ShowImageBorder)
            {
                RectangleF border = _viewport.GetImageRectInControl();
                using (Pen pen = new Pen(_renderOptions.ImageBorderColor, 1f))
                {
                    g.DrawRectangle(pen, border.X, border.Y, border.Width, border.Height);
                }
            }
        }

        /// <summary>放大到足够倍率时绘制像素网格。</summary>
        private void DrawPixelGrid(Graphics g)
        {
            if (_image == null || !_renderOptions.ShowPixelGrid) return;
            if (_viewport.Zoom < _renderOptions.PixelGridMinZoom) return;

            RectangleF visible = _viewport.GetVisibleImageRect(ClientSize);
            int x0 = (int)Math.Max(0, Math.Floor(visible.Left));
            int y0 = (int)Math.Max(0, Math.Floor(visible.Top));
            int x1 = (int)Math.Min(_image.Width, Math.Ceiling(visible.Right));
            int y1 = (int)Math.Min(_image.Height, Math.Ceiling(visible.Bottom));

            if (x1 - x0 > 300 || y1 - y0 > 300) return; // 网格过密则跳过

            RectangleF imgRect = _viewport.GetImageRectInControl();
            using (Pen pen = new Pen(_renderOptions.PixelGridColor, 1f))
            {
                for (int x = x0; x <= x1; x++)
                {
                    float cx = _viewport.ImageToControl(x, 0f).X;
                    g.DrawLine(pen, cx, Math.Max(0f, imgRect.Top), cx, Math.Min(ClientSize.Height, imgRect.Bottom));
                }
                for (int y = y0; y <= y1; y++)
                {
                    float cy = _viewport.ImageToControl(0f, y).Y;
                    g.DrawLine(pen, Math.Max(0f, imgRect.Left), cy, Math.Min(ClientSize.Width, imgRect.Right), cy);
                }
            }
        }

        private void DrawShapes(Graphics g)
        {
            foreach (IGeometryShape s in _shapes)
            {
                if (s == null || !s.Visible) continue;
                s.Draw(g, _viewport, _renderOptions);
            }

            if (_previewShape != null)
                _previewShape.Draw(g, _viewport, _renderOptions);
        }

        /// <summary>绘制选中图形的控制点。控制点尺寸固定为屏幕像素，不随缩放变化。</summary>
        private void DrawHandles(Graphics g)
        {
            float hs = Math.Max(_renderOptions.HandleSize, 3f);

            foreach (IGeometryShape s in _shapes)
            {
                if (s == null || !s.Visible || !s.IsSelected || !s.IsEditable) continue;

                IList<ShapeHandle> handles = s.GetHandles();
                for (int i = 0; i < handles.Count; i++)
                {
                    ShapeHandle h = handles[i];
                    PointF p = _viewport.ImageToControl(h.Position);
                    var rect = new RectangleF(p.X - hs * 0.5f, p.Y - hs * 0.5f, hs, hs);

                    Color fill = (h.Kind == HandleKind.Rotate) ? Color.Gold : _renderOptions.HandleFillColor;

                    using (Brush brush = new SolidBrush(fill))
                        g.FillRectangle(brush, rect);
                    using (Pen pen = new Pen(_renderOptions.HandleBorderColor, 1f))
                        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                }
            }
        }

        private void DrawCrosshair(Graphics g)
        {
            if (!_renderOptions.ShowCrosshair || !_mouseInside) return;

            using (Pen pen = new Pen(_renderOptions.CrosshairColor, 1f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, 0, _mouseControlPoint.Y, ClientSize.Width, _mouseControlPoint.Y);
                g.DrawLine(pen, _mouseControlPoint.X, 0, _mouseControlPoint.X, ClientSize.Height);
            }
        }

        /// <summary>
        /// 在右下角绘制鼠标位置面板：图像坐标 + 像素值。
        /// 灰度图显示 <c>Gray: 值</c>，彩色图显示 <c>Color: (R, G, B)</c>。
        /// </summary>
        private void DrawHud(Graphics g)
        {
            if (!_renderOptions.ShowHud || !_mouseInside) return;

            string line1 = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "X: {0:F1}    Y: {1:F1}", _mouseImagePoint.X, _mouseImagePoint.Y);

            bool isGray = (_image != null) && _imageIsGray;
            string line2;
            Color swatch = Color.Transparent;

            if (_mousePixelColor.HasValue)
            {
                Color c = _mousePixelColor.Value;
                if (isGray)
                {
                    // 灰度调色板下 R = G = B，任取其一即可
                    int v = c.R;
                    line2 = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture, "Gray: {0}", v);
                    swatch = Color.FromArgb(v, v, v);
                }
                else
                {
                    line2 = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Color: ({0}, {1}, {2})", c.R, c.G, c.B);
                    swatch = Color.FromArgb(c.R, c.G, c.B);
                }
            }
            else
            {
                line2 = isGray ? "Gray: --" : "Color: --";
            }

            const float margin = 8f;
            const float padding = 7f;
            const float swatchSize = 14f;
            const float lineGap = 3f;

            using (Font font = new Font(Font.FontFamily, Math.Max(Font.Size, 8.5f), Font.Style))
            {
                SizeF size1 = g.MeasureString(line1, font);
                SizeF size2 = g.MeasureString(line2, font);

                float textW = Math.Max(size1.Width, size2.Width);
                float lineH = Math.Max(size1.Height, size2.Height);

                float w = padding * 3f + swatchSize + textW;
                float h = padding * 2f + lineH * 2f + lineGap;

                float x = Math.Max(0f, ClientSize.Width - w - margin);
                float y = Math.Max(0f, ClientSize.Height - h - margin);

                using (Brush back = new SolidBrush(_renderOptions.HudBackColor))
                    g.FillRectangle(back, x, y, w, h);
                using (Pen border = new Pen(_renderOptions.HudBorderColor, 1f))
                    g.DrawRectangle(border, x, y, w, h);

                float textX = x + padding + swatchSize + padding;

                using (Brush fore = new SolidBrush(_renderOptions.HudForeColor))
                {
                    g.DrawString(line1, font, fore, textX, y + padding);
                    g.DrawString(line2, font, fore, textX, y + padding + lineH + lineGap);
                }

                // 左侧色块：直观预览当前像素
                float sx = x + padding;
                float sy = y + (h - swatchSize) * 0.5f;
                using (Brush fill = new SolidBrush(swatch))
                    g.FillRectangle(fill, sx, sy, swatchSize, swatchSize);
                using (Pen pen = new Pen(Color.FromArgb(130, 255, 255, 255), 1f))
                    g.DrawRectangle(pen, sx, sy, swatchSize, swatchSize);
            }
        }

        /// <summary>
        /// 当前缩放真正会用到的金字塔层数（0 = 连最细层都不够，直接用原图）。
        /// <para>第 i 层的缩放是 1/2^(i+1)，只有 ≥ zoom 的层才会被 <see cref="SelectPyramidLevel"/> 选中。
        /// 例：fit 到窗口时 zoom≈0.15，只需要 2 层（1/2、1/4），
        /// 原来却无条件把 12 层全建出来 —— 每帧白算 10 层。</para>
        /// </summary>
        private static int NeededPyramidLevels(float zoom)
        {
            if (zoom <= 0f || zoom >= 0.5f) return 0;

            int levels = 0;
            float scale = 0.5f;                       // 第 0 层的缩放
            while (scale >= zoom && levels < MaxPyramidLevels)
            {
                levels++;
                scale *= 0.5f;
            }
            return levels;
        }

        /// <summary>
        /// 生成图像金字塔：每一级是上一级的 1/2，第 i 级缩放为 1/2^(i+1)。
        /// 逐级生成（而非每级都从原图缩），总代价约为原图的 1/3。
        /// <para>【按需生成】只建到 <paramref name="neededLevels"/> 层为止：连续运行时每帧都会
        /// <see cref="SetImage"/>，若每帧都把 12 层重建一遍，单是重采样就吃掉大量 CPU。
        /// 已建层数够用时直接复用（层是前缀关系，内容与全量构建一致）。</para>
        /// </summary>
        private void EnsurePyramid(int neededLevels)
        {
            if (_image == null || neededLevels <= 0) return;
            if (_pyramid != null && !_pyramidDirty && _pyramid.Count >= neededLevels) return;

            ClearPyramid();
            _pyramid = new List<Bitmap>(neededLevels);

            Bitmap current = _image;
            for (int i = 0; i < neededLevels; i++)
            {
                int w = current.Width / 2;
                int h = current.Height / 2;
                if (w < MinPyramidSize || h < MinPyramidSize) break;

                var next = new Bitmap(w, h);
                using (Graphics g = Graphics.FromImage(next))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(current, new Rectangle(0, 0, w, h));
                }

                _pyramid.Add(next);
                current = next; // 下一级从上一级生成，数据量迅速变小
            }

            _pyramidDirty = false;
        }

        /// <summary>释放金字塔各层。</summary>
        private void ClearPyramid()
        {
            if (_pyramid == null) return;
            foreach (Bitmap b in _pyramid)
            {
                if (b != null) b.Dispose();
            }
            _pyramid.Clear();
            _pyramid = null;
        }

        /// <summary>
        /// 挑选「分辨率够用且最粗」的金字塔层（层缩放 ≥ 当前 zoom）。
        /// 没有合适层级时返回 null，调用方退回原图。
        /// </summary>
        private Bitmap SelectPyramidLevel(float zoom, out float levelScale)
        {
            levelScale = 1f;
            if (_pyramid == null || _pyramid.Count == 0) return null;

            // 从最粗层往最细层找，第一个满足分辨率不低于显示需求的层
            for (int i = _pyramid.Count - 1; i >= 0; i--)
            {
                float s = 1f / (1 << (i + 1));
                if (s >= zoom)
                {
                    levelScale = s;
                    return _pyramid[i];
                }
            }

            // zoom 已接近 1，连最细层都不够，直接用原图
            return null;
        }

        /// <summary>
        /// 索引色位图（1/4/8bpp，灰度图常见）在 GDI+ 中缩放绘制极慢——
        /// 每个像素都要查调色板，超大图上会明显卡顿。这里统一转成 24bpp，
        /// 走 GDI+ 的快速拉伸路径。无需转换时直接返回原引用。
        /// </summary>
        private static Bitmap ToDisplayBitmap(Bitmap src)
        {
            if (src == null) return null;
            if ((src.PixelFormat & PixelFormat.Indexed) == 0) return src;

            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(dst))
            {
                g.DrawImage(src, 0, 0, src.Width, src.Height);
            }
            return dst;
        }

        /// <summary>
        /// 判断位图是否为灰度图：16bpp 灰度，或调色板全部为灰阶的 8bpp 索引图。
        /// </summary>
        private static bool IsGrayImage(Bitmap bmp)
        {
            if (bmp == null) return false;

            PixelFormat pf = bmp.PixelFormat;
            if (pf == PixelFormat.Format16bppGrayScale) return true;
            if (pf != PixelFormat.Format8bppIndexed) return false;

            try
            {
                ColorPalette palette = bmp.Palette;
                int count = Math.Min(palette.Entries.Length, 256);
                for (int i = 0; i < count; i++)
                {
                    Color c = palette.Entries[i];
                    if (c.R != c.G || c.G != c.B) return false;
                }
                return true;
            }
            catch (Exception)
            {
                // 取不到调色板时按灰度处理（8bpp 通常就是灰度）
                return true;
            }
        }

        private void DrawEmptyHint(Graphics g)
        {
            if (_image != null) return;
            if (_cloudMode) return;     // 3D 点云模式：GL 子面板负责画面，不再叠"尚未载入图像"

            string text = "尚未载入图像" + Environment.NewLine +
                          "调用 SetImage() 或 SetCvImage() 显示图像";
            using (var font = new Font(Font.FontFamily, 10f))
            using (Brush brush = new SolidBrush(Color.FromArgb(150, 150, 160)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, font, brush, new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), sf);
            }
        }

        #endregion

        #region 事件触发

        private void OnShapesCollectionChanged(object sender, EventArgs e)
        {
            Invalidate();
            EventHandler handler = ShapesChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnZoomChanged()
        {
            EventHandler handler = ZoomChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnSelectionChanged()
        {
            EventHandler handler = SelectionChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnShapeCreated(IGeometryShape shape)
        {
            EventHandler<ShapeEventArgs> handler = ShapeCreated;
            if (handler != null) handler(this, new ShapeEventArgs(shape));
        }

        private void OnShapeCommitted(IGeometryShape shape)
        {
            EventHandler<ShapeEventArgs> handler = ShapeCommitted;
            if (handler != null) handler(this, new ShapeEventArgs(shape));
        }

        private void OnImageChanged()
        {
            EventHandler handler = ImageChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void OnMouseImageMove(PointF imagePt, Point screenPt)
        {
            EventHandler<ImagePointEventArgs> handler = MouseImageMove;
            if (handler != null)
            {
                handler(this, new ImagePointEventArgs(imagePt, screenPt, _mousePixelColor));
            }
        }

        #endregion
    }
}
