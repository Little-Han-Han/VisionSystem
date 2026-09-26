# API 参考

命名空间：

```csharp
using VisionSystem.Controls;              // ImageViewer, InteractionMode, 事件参数
using VisionSystem.Controls.Geometry;     // 图形类与控制点
using VisionSystem.Controls.Interop;      // CvInterop, CvPixelLayout
using VisionSystem.Controls.Rendering;    // Viewport, RenderOptions
```

---

## 一、ImageViewer

继承自 `System.Windows.Forms.Control`。

### 构造

```csharp
public ImageViewer()
```

### 属性

| 属性 | 类型 | 说明 |
|---|---|---|
| `Image` | `Bitmap` | 当前显示的图像。赋值等价于 `SetImage(value)`。 |
| `Shapes` | `ShapeCollection` | 图形集合，增删改自动触发重绘。 |
| `Options` | `RenderOptions` | 渲染选项（背景色、网格、控制点外观等）。 |
| `Viewport` | `Viewport` | 视口对象，含缩放 / 平移状态与坐标换算。 |
| `Mode` | `InteractionMode` | 交互模式，默认 `Select`。 |
| `Zoom` | `float` | 缩放倍率，1 = 1:1。设置时以控件中心为锚点。 |
| `ZoomStep` | `float` | 滚轮每格缩放倍率，默认 1.15。 |
| `OwnsImage` | `bool` | 是否拥有图像所有权（切换 / 释放时自动 `Dispose`），默认 `true`。 |
| `AutoFitOnResize` | `bool` | 控件尺寸变化时是否重新适应窗口，默认 `false`。 |
| `NewShapeColor` | `Color` | 新建图形的默认线色，默认 `Lime`。 |
| `NewShapeLineWidth` | `float` | 新建图形的默认线宽（屏幕像素），默认 2。 |
| `ShowCrosshair` | `bool` | 是否显示跟随鼠标的辅助十字线（可用右键菜单切换）。 |
| `ShowHud` | `bool` | 右下角是否显示鼠标图像坐标与像素值（灰度显示 `Gray`，彩色显示 `Color`）。 |
| `ViewerContextMenu` | `ContextMenuStrip` | 控件内置的右键菜单对象，可往里追加自定义菜单项。 |
| `MouseImagePosition` | `PointF` | 鼠标当前所在的图像坐标。 |
| `ImageSize` | `Size` | 图像尺寸，无图像时为 `Empty`。 |

### 方法

| 方法 | 说明 |
|---|---|
| `SetImage(Bitmap image, bool autoFit = true)` | 设置显示的图像，`autoFit` 为 true 时自动缩放至完整可见。 |
| `SetCvImage(IntPtr data, int width, int height, int stride, CvPixelLayout layout, bool autoFit = true)` | 直接显示 OpenCV `Mat` 的像素数据（内部深拷贝）。`stride` 传 0 表示无行填充。 |
| `ClearImage()` | 清空图像，保留图形。 |
| `GetImageBytes(CvPixelLayout layout = CvPixelLayout.Bgr24)` | 把当前图像导出为连续像素数组，可直接构造 `Mat`。无图像返回 `null`。 |
| `GetPixelColor(PointF imagePoint)` | 读取图像坐标处的颜色，越界返回 `null`。 |
| `FitToWindow()` | 缩放至完整可见并居中。 |
| `SetZoom(float zoom)` | 设置缩放倍率（以控件中心为锚点）。 |
| `ZoomIn()` / `ZoomOut()` | 放大 / 缩小一档。 |
| `ZoomActual()` | 恢复 1:1。 |
| `CenterOn(PointF imagePoint)` | 把指定图像坐标滚动到控件中心。 |
| `ImageToControl(PointF p)` | 图像坐标 → 控件坐标。 |
| `ControlToImage(PointF p)` | 控件坐标 → 图像坐标。 |
| `SelectShape(IGeometryShape shape)` | 选中指定图形，同时取消其它图形的选中。 |
| `GetSelectedShape()` | 返回第一个选中的图形，无则返回 `null`。 |
| `DeleteSelected()` | 删除所有选中图形，返回删除数量。 |
| `ClearShapes()` | 移除全部图形。 |
| `ExportShapesToCvText()` | 把所有可见图形导出为 OpenCV 参数文本，每行一个。 |
| `SaveImageToFile()` | 弹出另存为对话框保存当前图像（不含图形叠加）。成功返回 `true`。 |
| `SaveSnapshotToFile()` | 弹出另存为对话框保存当前视图截图（含图形、网格、背景）。成功返回 `true`。 |
| `CreateSnapshot()` | 生成当前可见区域的 `Bitmap` 快照，不弹对话框，适合程序内部使用。 |
| `InvalidateImageLayer()` | 强制重建静态内容缓存层。直接修改 `Options` 的背景色 / 网格 / 插值模式后需调用，仅 `Invalidate()` 不会重建缓存。 |

### 事件

| 事件 | 参数 | 触发时机 |
|---|---|---|
| `ZoomChanged` | `EventHandler` | 缩放倍率变化。 |
| `ShapesChanged` | `EventHandler` | 图形增删改，或几何被拖拽修改。 |
| `SelectionChanged` | `EventHandler` | 选中项变化。 |
| `ShapeCreated` | `EventHandler<ShapeEventArgs>` | 用户拖拽新建了图形。 |
| `ImageChanged` | `EventHandler` | 显示的图像被替换。 |
| `MouseImageMove` | `EventHandler<ImagePointEventArgs>` | 鼠标在控件上移动，携带图像坐标与像素颜色。 |

### 事件参数类型

```csharp
public class ShapeEventArgs : EventArgs
{
    public IGeometryShape Shape { get; }
}

public class ImagePointEventArgs : EventArgs
{
    public PointF ImagePoint { get; }   // 图像坐标
    public Point ControlPoint { get; }  // 控件坐标
    public Color? PixelColor { get; }   // 像素颜色，越界为 null
}
```

---

## 二、图形类

### 2.1 IGeometryShape（统一接口）

| 成员 | 类型 | 说明 |
|---|---|---|
| `Name` | `string` | 图形名称。 |
| `Tag` | `object` | 附加业务数据。 |
| `StrokeColor` | `Color` | 线条颜色。 |
| `LineWidth` | `float` | 线宽，单位**屏幕像素**。 |
| `Visible` | `bool` | 是否绘制。 |
| `IsSelected` | `bool` | 是否选中（选中后绘制控制点）。 |
| `IsFilled` | `bool` | 是否填充。同时决定命中区域是「整个内部」还是「仅边缘」。 |
| `FillColor` | `Color` | 填充色。 |
| `IsEditable` | `bool` | 是否可被鼠标选中 / 编辑。 |
| `Bounds` | `RectangleF` | 图像坐标下的包围盒。 |
| `Draw(Graphics, Viewport, RenderOptions)` | `void` | 绘制。 |
| `HitTest(PointF imagePt, float tolerance)` | `bool` | 命中测试，`tolerance` 为图像坐标单位。 |
| `Translate(float dx, float dy)` | `void` | 整体平移。 |
| `GetHandles()` | `IList<ShapeHandle>` | 获取控制点列表。 |
| `SetHandlePosition(int index, PointF pt)` | `void` | 把指定控制点拖到新位置。 |
| `Clone()` | `IGeometryShape` | 深拷贝。 |
| `ToGeometryString()` | `string` | 易读的几何参数文本。 |

### 2.2 CircleShape

```csharp
public CircleShape()
public CircleShape(PointF center, float radius)
```

| 成员 | 类型 | 说明 |
|---|---|---|
| `Center` | `PointF` | 圆心（图像坐标）。 |
| `Radius` | `float` | 半径，自动限制 ≥ `MinRadius`(1)。 |
| `Diameter` | `float` | 直径（读写，内部换算为半径）。 |
| `MinRadius` | `const float` | 最小半径，1。 |

**控制点**：`0` = 圆心（移动），`1`~`4` = 圆周东 / 南 / 西 / 北四点（改半径）。

### 2.3 RectangleShape

```csharp
public RectangleShape()
public RectangleShape(float x, float y, float width, float height)
public RectangleShape(RectangleF rect)
```

| 成员 | 类型 | 说明 |
|---|---|---|
| `X` / `Y` | `float` | 左上角坐标。 |
| `Width` / `Height` | `float` | 宽高，自动取绝对值并限制 ≥ `MinSize`(1)。 |
| `Rect` | `RectangleF` | 以矩形形式读写。 |
| `MinSize` | `const float` | 最小边长，1。 |

**控制点**：`0`~`3` = 左上 / 右上 / 右下 / 左下四角，`4`~`7` = 上 / 右 / 下 / 左四边中点。
拖拽时允许越过对角，会自动翻转为正尺寸。

### 2.4 RotatedRectShape

OpenCV `RotatedRect` 的等价表示。

```csharp
public RotatedRectShape()
public RotatedRectShape(PointF center, float width, float height, float angleDeg)
```

| 成员 | 类型 | 说明 |
|---|---|---|
| `Center` | `PointF` | 中心点。 |
| `Width` / `Height` | `float` | 沿**局部** X / Y 轴的长度。 |
| `AngleDeg` | `float` | 旋转角度（度），自动归一化到 (-180, 180]。 |
| `AngleRad` | `double` | 旋转角度（弧度）。 |
| `RotateHandleIndex` | `const int` | 旋转手柄序号，固定为 4。 |
| `GetCorners()` | `PointF[]` | 四个角点，顺序：左上 → 右上 → 右下 → 左下。 |
| `ToLocal(PointF)` | `PointF` | 图像坐标 → 以中心为原点、已消除旋转的局部坐标。 |
| `ToWorld(PointF)` | `PointF` | 局部坐标 → 图像坐标。 |

**控制点**：`0`~`3` = 四角（对角固定式缩放，拖角时对角不动），`4` = 旋转手柄（金色，位于局部 +X 轴外侧）。
拖动旋转手柄时，角度直接取 `atan2(鼠标 - 中心)`。

### 2.5 RingShape

同心圆环带，常用于环形检测区、同心度测量等场景。内半径为 0 时退化为实心圆。

```csharp
public RingShape()
public RingShape(PointF center, float outerRadius, float innerRadius)
```

| 成员 | 类型 | 说明 |
|---|---|---|
| `Center` | `PointF` | 圆心（图像坐标）。 |
| `OuterRadius` | `float` | 外半径，自动保证不小于内半径 + `MinGap`。 |
| `InnerRadius` | `float` | 内半径，自动裁剪到 [0, 外半径 − `MinGap`]，取 0 即为实心圆。 |
| `Thickness` | `float` | 环带厚度（外半径 − 内半径）。写入时保持外半径不变，反推内半径。 |
| `MinRadius` | `const float` | 最小外半径，1。 |
| `MinGap` | `const float` | 环带最小厚度，1。 |

**控制点**：`0` = 圆心（整体移动），`1`~`4` = 外圆东 / 南 / 西 / 北（改外半径），
`5`~`8` = 内圆东 / 南 / 西 / 北（改内半径）。

**命中规则**：`IsFilled = true` 时命中整个环带；`IsFilled = false` 时只命中内外两条圆周。
填充通过两个同心圆路径 + `FillMode.Alternate` 实现，内孔自动镂空。

### 2.6 ShapeHandle

| 成员 | 类型 | 说明 |
|---|---|---|
| `Position` | `PointF` | 控制点位置（**图像坐标**）。 |
| `Kind` | `HandleKind` | `Resize` / `Rotate` / `Move`。 |
| `Index` | `int` | 图形内序号，用于 `SetHandlePosition`。 |
| `Tag` | `object` | 附加标识（如 `"TL"`、`"Rotate"`）。 |

---

## 三、ShapeCollection

继承 `Collection<IGeometryShape>`。

| 成员 | 说明 |
|---|---|
| `AddRange(IEnumerable<IGeometryShape>)` | 批量添加。 |
| `FindByName(string)` | 按名称查找第一个匹配项。 |
| `GetSelected()` | 返回所有选中图形。 |
| `ClearSelection()` | 取消全部选中。 |
| `RemoveSelected()` | 移除所有选中项，返回数量。 |
| `NotifyChanged()` | 手动触发变更通知（直接修改图形几何后调用以请求重绘）。 |
| `event CollectionChanged` | 集合增删改时触发。 |

---

## 四、Viewport

| 成员 | 类型 | 说明 |
|---|---|---|
| `ImageSize` | `Size` | 图像尺寸。 |
| `Zoom` | `float` | 缩放倍率，自动限制在 `[MinZoom, MaxZoom]`。 |
| `OffsetX` / `OffsetY` | `float` | 平移量（控件坐标）。 |
| `Offset` | `PointF` | 平移量读写。 |
| `ImageOrigin` | `PointF` | 图像左上角在控件中的位置。 |
| `MinZoom` / `MaxZoom` | `const float` | 0.01 / 256。 |
| `ImageToControl(PointF)` | `PointF` | 图像坐标 → 控件坐标。 |
| `ControlToImage(PointF)` | `PointF` | 控件坐标 → 图像坐标。 |
| `ImageToControl(RectangleF)` | `RectangleF` | 矩形变换。 |
| `ControlToImage(RectangleF)` | `RectangleF` | 矩形逆变换。 |
| `ScaleLength(float)` | `float` | 图像长度 → 屏幕长度。 |
| `UnscaleLength(float)` | `float` | 屏幕长度 → 图像长度（容差换算用这个）。 |
| `GetImageRectInControl()` | `RectangleF` | 图像整体在控件中的矩形。 |
| `GetVisibleImageRect(Size)` | `RectangleF` | 控件可视区对应的图像矩形（可能超出边界）。 |
| `ZoomAt(PointF anchor, float factor)` | `void` | 以锚点为中心缩放——滚轮缩放的核心。 |
| `SetZoomAt(float zoom, PointF anchor)` | `void` | 设为指定倍率并保持锚点不动。 |
| `CenterImage(Size)` | `void` | 居中（不改变缩放）。 |
| `Fit(Size, float marginRatio = 0.98f)` | `void` | 缩放至完整可见并居中。 |
| `ResetZoom(Size)` | `void` | 恢复 1:1 并以控件中心为锚点。 |
| `CenterOn(PointF, Size)` | `void` | 把图像点滚动到控件中心。 |
| `Reset()` | `void` | 重置为 1:1、无平移。 |

---

## 五、RenderOptions

| 属性 | 默认值 | 说明 |
|---|---|---|
| `BackdropColor` | `#202024` | 控件背景色。 |
| `ImageBorderColor` | `#5A5A64` | 图像边框色。 |
| `ShowImageBorder` | `true` | 是否绘制图像边框。 |
| `ShowPixelGrid` | `true` | 是否绘制像素网格。 |
| `PixelGridMinZoom` | `8` | 达到该倍率才绘制网格。 |
| `PixelGridColor` | 半透明灰蓝 | 网格颜色。 |
| `ShowCrosshair` | `false` | 是否绘制鼠标十字线。 |
| `CrosshairColor` | 半透明青色 | 十字线颜色。 |
| `HandleSize` | `7` | 控制点边长（屏幕像素）。 |
| `HandleFillColor` | `White` | 控制点填充色（旋转手柄固定为 `Gold`）。 |
| `HandleBorderColor` | `#1E1E1E` | 控制点边框色。 |
| `SelectionColor` | `#00C8FF` | 选中高亮色。 |
| `AntiAlias` | `true` | 图形抗锯齿。 |
| `ShowHud` | `true` | 右下角是否显示鼠标坐标 + 像素值面板。 |
| `HudBackColor` | 半透明深色 | HUD 面板背景色。 |
| `HudForeColor` | `Gainsboro` | HUD 面板文字色。 |
| `HudBorderColor` | 半透明灰蓝 | HUD 面板边框色。 |
| `ZoomInInterpolation` | `NearestNeighbor` | 放大（Zoom ≥ 1）时的插值模式。 |
| `ZoomOutInterpolation` | `HighQualityBicubic` | 缩小（Zoom < 1）时的插值模式。 |
| `Reset()` | — | 恢复全部默认值。 |

---

## 六、CvInterop

### CvPixelLayout 枚举

| 值 | 对应 OpenCV 类型 | 说明 |
|---|---|---|
| `Gray8` | `CV_8UC1` | 单通道灰度（自动附加灰度调色板）。 |
| `Bgr24` | `CV_8UC3` | B-G-R，OpenCV 默认顺序。 |
| `Rgb24` | `CV_8UC3` | R-G-B，已做过通道转换的数据。 |
| `Bgra32` | `CV_8UC4` | B-G-R-A，第 4 通道强制置 255。 |

### 方法

| 方法 | 说明 |
|---|---|
| `ToBitmap(IntPtr data, int width, int height, int stride, CvPixelLayout layout)` | 把 OpenCV 像素缓冲区转为 `Bitmap`（深拷贝）。转换后可立即释放 `Mat`。 |
| `FromPixels(byte[] pixels, int width, int height, CvPixelLayout layout)` | 把连续像素数组转为 `Bitmap`。 |
| `ToBytes(Bitmap bmp, CvPixelLayout layout)` | 把 `Bitmap` 导出为指定排布的连续数组。 |
| `ToBgrBytes(Bitmap bmp)` | 快捷方法，等价于 `ToBytes(bmp, Bgr24)`。 |
| `ToGrayBytes(Bitmap bmp)` | 快捷方法，等价于 `ToBytes(bmp, Gray8)`。灰度权重与 OpenCV `COLOR_RGB2GRAY` 一致。 |
| `GetBytesPerPixel(CvPixelLayout)` | 每像素字节数。 |
| `GetPixelFormat(CvPixelLayout)` | 对应的 GDI+ `PixelFormat`。 |
| `ToCvParameterString(IGeometryShape shape)` | 把图形转成 OpenCV 参数文本，如 `RotatedRect(center=(...), size=(...), angle=...)`。 |

### 异常

- `ArgumentNullException`：`data` 或 `pixels` 为空。
- `ArgumentOutOfRangeException`：宽高 ≤ 0，或 `layout` 非法。
- `ArgumentException`：`stride` 小于一行像素所需的最小字节数（通常是把 `CV_8UC3` 当成 `Gray8` 解析了）。

---

## 七、InteractionMode 枚举

| 值 | 说明 |
|---|---|
| `Select` | 选择并编辑图形（默认）。 |
| `Pan` | 平移。 |
| `DrawCircle` | 绘制圆形。 |
| `DrawRectangle` | 绘制轴对齐矩形。 |
| `DrawRotatedRect` | 绘制旋转矩形。 |
| `DrawRing` | 绘制圆环：按下定圆心，拖动定外半径，内半径默认取外半径的一半。 |

---

## 八、完整示例

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;
using VisionSystem.Controls.Interop;

public partial class Form1 : Form
{
    private ImageViewer _viewer;

    public Form1()
    {
        InitializeComponent();

        _viewer = new ImageViewer { Dock = DockStyle.Fill };
        _viewer.Options.ShowCrosshair = true;
        Controls.Add(_viewer);

        _viewer.SetImage(new Bitmap("board.jpg"));

        // 预置一个 ROI
        var roi = new RotatedRectShape(new PointF(400, 300), 200, 100, 15f);
        roi.Name = "ROI-1";
        roi.StrokeColor = Color.Lime;
        roi.IsFilled = true;
        _viewer.Shapes.Add(roi);

        // 画完自动回到选择模式
        _viewer.ShapeCreated += (s, e) => _viewer.Mode = InteractionMode.Select;

        // 实时显示鼠标位置与像素值
        _viewer.MouseImageMove += (s, e) =>
        {
            Text = string.Format("X={0:F1} Y={1:F1} {2}",
                e.ImagePoint.X, e.ImagePoint.Y, e.PixelColor);
        };

        // 读取编辑结果
        _viewer.SelectionChanged += (s, e) =>
        {
            IGeometryShape sel = _viewer.GetSelectedShape();
            if (sel != null)
                Console.WriteLine(CvInterop.ToCvParameterString(sel));
        };
    }
}
```
