# VisionSystem 项目长期约定

## 技术栈

- 目标框架 **.NET Framework 4.8 + WinForms**
- Visual Studio 2022 Community 位于 `D:\Program Files\Microsoft Visual Studio\2022\Community`（D 盘，非 C 盘）
- 项目格式：**传统（非 SDK）csproj**，刻意不用 SDK 风格 —— 无需 NuGet restore、离线可编译、设计器兼容性最好
- 控件库 **零第三方依赖**（纯 GDI+）；OpenCV 互操作基于 `IntPtr + stride`，不引入 OpenCvSharp 等 NuGet 包

## 构建方式

- 双击根目录 `build.bat`，或在 Bash 中直接执行该 bat 的完整路径
- **不能**在命令行直接调用 MSBuild / devenv / cmd —— 会被安全策略以 LOLBin 为由拦截，
  `dangerouslyDisableSandbox` 也无法绕过（属内容级检测）
- bat 文件内容必须是**纯 ASCII**：中文在 cmd 的 GBK 代码页下会乱码并导致命令解析失败

## 坐标约定（易错点）

- 图形几何属性（Center/Width/AngleDeg 等）一律使用**图像坐标**
- `LineWidth`、`HandleSize`、命中测试容差一律使用**屏幕像素**，保证任意缩放下手感一致
- 换算：`control = image * Zoom + Offset`

## 代码修改注意

- 直接修改图形几何属性（如 `circle.Radius = 50`）不会自动重绘，需调用 `Shapes.NotifyChanged()`
- 集合的增删改会自动触发重绘
