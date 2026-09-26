# VisionSystem — 机器视觉图像控件库

面向 .NET Framework 4.8 / WinForms 的图像显示与几何标注控件库。
提供图像缩放平移、几何图形绘制与拖拽编辑，并可直接与 OpenCV 的 `Mat` 数据互通。

- 目标框架：**.NET Framework 4.8**
- 开发工具：**Visual Studio 2022 Community**（`D:\Program Files\Microsoft Visual Studio\2022\Community`）
- 第三方依赖：**无**（纯 GDI+ 实现，离线可编译）

---

## 功能特性

### 图像显示
- 双缓冲绘制，无闪烁
- **只渲染可见区域**——放大到几十倍时依然流畅，不会因大图卡顿
- 放大时默认邻近采样（保持像素清晰），缩小时高质量双三次插值
- 放大到 8 倍以上自动显示像素网格
- 可选十字线、图像边框
- **右下角 HUD** 实时显示鼠标所在的图像坐标与像素值：
  灰度图显示 `Gray: 173`，彩色图显示 `Color: (201, 96, 44)`，并带色块预览

### 视图操作
- 滚轮缩放，**以鼠标位置为锚点**（指哪放大哪）
- `Ctrl + 滚轮` 快速缩放
- 中键 / 右键 / `空格 + 左键` 三种平移方式
- 适应窗口、1:1、按倍率缩放、居中到指定点

### 几何图形
| 图形 | 类 | 控制点 |
|---|---|---|
| 圆形 | `CircleShape` | 圆心 1 个 + 圆周 4 个（改半径） |
| 轴对齐矩形 | `RectangleShape` | 4 角 + 4 边中点 |
| 旋转矩形 | `RotatedRectShape` | 4 角（对角固定式缩放）+ 1 个旋转手柄 |
| 圆环 | `RingShape` | 圆心 1 个 + 外圆 4 个（改外半径）+ 内圆 4 个（改内半径） |

- 图形可整体拖拽移动，也可拖动控制点改变尺寸 / 半径 / 角度 / 内外半径
- 命中测试带屏幕像素容差，支持填充 / 空心两种命中模式
- 线宽以屏幕像素为单位，**缩放时线条不会变粗**
- 每个图形可独立设置颜色、线宽、是否填充、是否可编辑

### 右键菜单

在控件上**右键单击**（不拖动）即弹出内置菜单：

| 菜单项 | 功能 |
|---|---|
| 保存图像(&S) | 把当前图像（不含图形叠加）另存为 PNG / JPEG / BMP / TIFF |
| 保存视图截图(&V) | 把当前所见画面（含图形、网格、背景）另存为文件 |
| 自适应图像(&F) | 缩放至完整可见并居中 |
| 显示辅助十字线(&C) | 开关跟随鼠标的十字线，勾选状态实时同步 |

右键**拖动**仍然是平移画布，不会弹出菜单——点击与拖拽互不干扰。
菜单对象可通过 `ViewerContextMenu` 属性获取，可往里追加自己的菜单项。

### OpenCV 互操作
`CvInterop` 基于 `Mat.data` 指针 + `step` 行字节数工作，**不需要安装任何 NuGet 包**：

- `CV_8UC1`（灰度）、`CV_8UC3`（BGR/RGB）、`CV_8UC4`（BGRA）全覆盖
- 自动处理 BGR ↔ RGB 通道交换、灰度调色板、行尾填充（stride ≠ width × bpp）
- 反向导出：Bitmap → 连续 BGR / 灰度数组，可直接构造 `Mat`

---

## 目录结构

```
VisionSystem/
├── VisionSystem.sln                    解决方案
├── build.bat                           一键构建脚本（双击即可编译）
├── README.md                           本文件
├── docs/
│   ├── 使用说明.md                      操作方式、集成步骤、OpenCV 对接
│   └── API参考.md                       完整 API 清单
└── src/
    ├── VisionSystem.Controls/          控件库（类库，产出 DLL）
    │   ├── ImageViewer.cs              核心控件
    │   ├── Rendering/
    │   │   ├── Viewport.cs             坐标变换 / 缩放平移
    │   │   └── RenderOptions.cs        渲染选项
    │   ├── Geometry/
    │   │   ├── IGeometryShape.cs       图形统一接口
    │   │   ├── ShapeHandle.cs          控制点
    │   │   ├── CircleShape.cs          圆形
    │   │   ├── RectangleShape.cs       矩形
    │   │   ├── RingShape.cs            圆环
    │   │   ├── RotatedRectShape.cs     旋转矩形
    │   │   └── ShapeCollection.cs      图形集合（带变更事件）
    │   └── Interop/
    │       └── CvInterop.cs            OpenCV 数据互操作
    └── VisionSystem.Demo/              演示程序（WinExe）
        ├── Program.cs
        └── MainForm.cs                 完整用法示例
```

---

## 快速开始

### 1. 构建

双击根目录的 `build.bat`，或在命令行执行：

```bat
build.bat            :: 编译 Debug
build.bat Release    :: 编译 Release
```

构建产物：

```
src\VisionSystem.Controls\bin\Debug\VisionSystem.Controls.dll
src\VisionSystem.Demo\bin\Debug\VisionSystem.Demo.exe
```

### 2. 运行演示

直接运行 `src\VisionSystem.Demo\bin\Debug\VisionSystem.Demo.exe`，
或在 VS2022 中将 `VisionSystem.Demo` 设为启动项目后按 F5。

演示程序无需任何外部图片，启动即生成合成测试图，并预置了圆、矩形、旋转矩形各一个。

### 3. 在 VS2022 中打开

双击 `VisionSystem.sln`。控件库编译后，`ImageViewer` 会出现在**工具箱**中，
可直接拖拽到你自己的窗体上使用，也可以在属性窗口中调整 `Mode`、`ZoomStep`、`NewShapeColor` 等属性。

---

## 最小代码示例

```csharp
using System.Drawing;
using VisionSystem.Controls;
using VisionSystem.Controls.Geometry;
using VisionSystem.Controls.Interop;

// 1. 显示图像
imageViewer1.SetImage(new Bitmap("test.jpg"));

// 2. 直接显示 OpenCV 的 Mat（OpenCvSharp）
using (var mat = Cv2.ImRead("test.jpg", ImreadModes.Color))
{
    // mat.Data 是 IntPtr，mat.Step() 是行字节数
    imageViewer1.SetCvImage(mat.Data, mat.Width, mat.Height,
                            (int)mat.Step(), CvPixelLayout.Bgr24);
}

// 3. 用代码添加图形
var roi = new RotatedRectShape(new PointF(320, 240), 180, 90, 30f);
roi.Name = "检测区";
roi.StrokeColor = Color.Lime;
imageViewer1.Shapes.Add(roi);

// 4. 读取用户编辑后的结果
foreach (IGeometryShape s in imageViewer1.Shapes)
{
    Console.WriteLine(CvInterop.ToCvParameterString(s));
    // → RotatedRect(center=(320.000,240.000), size=(180.000,90.000), angle=30.000)
}
```

---

## 文档

- [使用说明](docs/使用说明.md) —— 鼠标键盘操作、集成到现有项目、OpenCV 对接方式、常见问题
- [API 参考](docs/API参考.md) —— 全部公开属性、方法、事件的参数说明
