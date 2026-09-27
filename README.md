# VisionSystem — 工业机器视觉软件

一套完整的工业视觉检测软件，采用 **C++ / OpenCV** 编写视觉算法、**C# / WinForms (.NET Framework 4.8)** 编写软件界面，通过 P/Invoke 调用 C++ 算法 DLL。整套代码包含在一个解决方案 `VisionSystem.sln` 内，共 4 个项目。

- 目标框架：**.NET Framework 4.8**
- 算法引擎：**C++ / OpenCV 4.5.5**
- 开发工具：**Visual Studio 2022 Community**（`D:\Program Files\Microsoft Visual Studio\2022\Community`）
- 平台：**全部 x64**（算法库为 x64，主程序 `PlatformTarget=x64`）
- 第三方依赖：**零 NuGet 包**（JSON 用 `DataContractJsonSerializer`，Modbus/TCP 自行实现），外部 DLL 统一收纳在 `ThirdParty/`

---

## 功能特性

### 图像显示控件库（VisionSystem.Controls）
可复用的图像显示与几何标注控件，主程序与各演示均依赖它。
- 双缓冲绘制、只渲染可见区域，8K 级大图缩放平移依然流畅
- 以鼠标位置为锚点的滚轮缩放、`Ctrl+滚轮` 快速缩放、中键/右键/空格平移
- 几何图形：圆形、轴对齐矩形、旋转矩形、圆环，可整体拖拽及拖动手柄改尺寸/半径/角度
- 右下角 HUD 实时显示鼠标图像坐标与像素值（灰度图 `Gray: 173`，彩色图 `Color: (R,G,B)`）
- 右键菜单：保存图像、保存视图截图、自适应图像、十字辅助线开关
- 与 OpenCV `Mat` 数据零拷贝互通（`CvInterop`，无需 NuGet）

### 工业视觉主程序（VisionPlatform）
- **登录 / 启动加载**：`LoginForm` + `SplashForm`
- **相机管理**：统一相机接口 `ICamera`，内置模拟相机 `SimCamera` 跑通全流程，海康 `HikCamera` 适配器（占位，见遗留问题）
- **通讯**：TCP 客户端/服务端、`ModbusTcpClient`（自行实现 MBAP+PDU，支持 0x03/0x06/0x10）
- **配方**：`Recipe` + `RecipeStore`，JSON 存取、当前配方切换自动同步参数
- **算法**：参数编辑 + 离线测试，**支持打开本地图片**（无需相机即可调试）
- **系统设置**：图片保存路径、保存格式（PNG/JPEG/BMP/TIFF）、保留天数（自动清理过期图片）、检测后自动保存与仅存 NG、循环运行前是否要求通讯已连接；全部参数按「系统 / 算法 / 界面」三类持久化到本地 JSON（`%AppData%/VisionPlatform/appconfig.json`），重启自动加载，无需重新配置
- **主运行界面** `MainForm`：布局可调的多显示视图（1x1 / 1x2 / 2x1 / 2x2 / 3x2 / 3x3，最多 9 个）、**每个视图可单独绑定相机**、单帧采集与多相机同步采集、循环运行/暂停/停止、数据统计（总数/OK/NG/良率/节拍）、运行日志

### C++ 算法库（VisionAlgo.Native）
预处理、Blob 分析、清晰度（边缘/模板）、圆拟合、直线拟合、模板匹配。导出为 x64 DLL，由 C# 通过唯一 P/Invoke 边界调用。

### 控件库演示（VisionSystem.Demo）
独立演示程序，展示 `ImageViewer` 的全部交互与图形编辑能力。

---

## 目录结构

```
VisionSystem/                              唯一解决方案根目录
├── VisionSystem.sln                       单一解决方案（4 个项目 + Solution Items）
├── build.bat                              一键构建（含代理变量清理）
├── README.md                              本文件
├── ThirdParty/                            所有外部依赖，按用途分次级目录
│   ├── OpenCV/                            OpenCV 4.5.5（include / lib/x64 已入库，bin/x64 需自备）
│   ├── HikVision/                         海康 MVS SDK（当前为空，附 README 说明）
│   ├── Communication/                     通讯类第三方库（当前无依赖）
│   └── README.md                          第三方依赖说明
├── bin/x64/{Debug,Release}/               算法 DLL 与 OpenCV 运行时（构建产物）
├── docs/                                  文档
│   ├── 平台架构.md                         架构、分层、模块职责、P/Invoke 规范
│   ├── 使用说明.md                        操作方式与集成步骤
│   ├── API参考.md                         控件库完整 API 清单
│   └── 遗留问题.md                        已知问题、TODO、占位实现
└── src/
    ├── VisionAlgo.Native/                 【C++ / OpenCV】算法 DLL
    │   ├── include/
    │   │   ├── vision_algo.h              聚合头（包含以下全部）
    │   │   └── algo/                      算法头文件
    │   │       ├── va_preprocess.h        预处理：灰度/模糊/二值/形态学
    │   │       ├── va_blob.h              Blob 分析
    │   │       ├── va_fit.h               圆/直线拟合
    │   │       ├── va_match.h             模板匹配
    │   │       └── va_measure.h           测量（清晰度等）
    │   ├── src/
    │   │   ├── vision_algo.cpp            导出层：句柄校验 + 转发
    │   │   └── algo/                      算法实现
    │   │       ├── algo_preprocess.cpp
    │   │       ├── algo_blob.cpp
    │   │       ├── algo_fit.cpp
    │   │       ├── algo_match.cpp
    │   │       └── algo_measure.cpp
    │   ├── Common/                        公共：导出宏/错误码/类型/生命周期/内部工具
    │   │   ├── va_export.h
    │   │   ├── va_error.h
    │   │   ├── va_types.h
    │   │   ├── va_lifecycle.h
    │   │   ├── va_internal.h
    │   │   └── va_internal.cpp
    │   ├── VisionAlgo.Native.vcxproj
    │   └── VisionAlgo.Native.vcxproj.filters
    ├── VisionSystem.Controls/              【C#】图像显示控件库
    │   ├── ImageViewer.cs                  核心控件
    │   ├── Geometry/                       IGeometryShape / ShapeHandle / CircleShape /
    │   │                                  RectangleShape / RotatedRectShape / RingShape / ShapeCollection
    │   ├── Interop/                        CvInterop（OpenCV 数据互操作）
    │   └── Rendering/                      Viewport（坐标变换）/ RenderOptions（渲染选项）
    ├── VisionSystem.Demo/                  【C#】控件库演示
    │   ├── MainForm.cs
    │   └── Program.cs
    └── VisionPlatform/                     【C#】工业视觉主程序
        ├── Program.cs
        ├── Core/                          Logger / AppPaths / Json / OpResult /
        │                                 AppSettings（三段式配置 AppConfig：System 系统 / Algorithm 算法 / Ui 界面 + ConfigStore）/ ImageStore（存图与清理）
        ├── Native/                        VisionAlgo（P/Invoke 封装 + 托管结果类型）
        ├── Camera/                        ICamera / SimCamera / HikCamera / CameraManager / CameraInfo
        ├── Communication/                 TcpClientSession / TcpServerSession / ModbusTcpClient /
        │                                 CommState（通讯全局状态）
        ├── Recipe/                        Recipe / RecipeStore
        ├── Vision/                        InspectionConfig / InspectionResult / InspectionEngine
        ├── Models/                        User
        └── UI/                            MainForm / CameraForm / AlgoForm / CommForm /
                                           RecipeForm / SettingsForm / LoginForm / SplashForm
                                           （各自 .cs + .Designer.cs）
```

> **说明**：所有窗体均采用 **Designer 模式**，拆分为 `XxxForm.cs`（业务逻辑）与 `XxxForm.Designer.cs`（布局），可在 VS 中直接拖拽调整界面。C++ 项目通过 `.vcxproj.filters` 维护解决方案资源管理器中的文件夹层级。

---

## 快速开始

### 1. 环境要求
- Windows + **Visual Studio 2022**（含"使用 C++ 的桌面开发"与".NET 桌面开发"工作负载）
- 已安装 OpenCV 4.5.5 的 x64 运行时（用于准备 `ThirdParty/OpenCV/bin/x64` 下的 DLL）

### 2. 准备 OpenCV 运行时 DLL
仓库已包含 OpenCV 的头文件与导入库（`ThirdParty/OpenCV/include`、`ThirdParty/OpenCV/lib/x64`），但运行时 DLL（约 61MB / 122MB）超过 GitHub 单文件 100MB 上限，未入库。构建前请先放入：

```bat
set OPENCV=D:\Users\admin\Downloads\opencv\build
xcopy /Y "%OPENCV%\x64\vc15\bin\opencv_world455.dll"   ThirdParty\OpenCV\bin\x64\
xcopy /Y "%OPENCV%\x64\vc15\bin\opencv_world455d.dll"  ThirdParty\OpenCV\bin\x64\
```

（只跑 Release 时只需 `opencv_world455.dll`；调试需要 `455d.dll`。）详见 `ThirdParty/README.md`。

### 3. 构建
双击根目录的 `build.bat`，或在命令行执行：

```bat
build.bat            :: 编译 Debug   | x64
build.bat Release    :: 编译 Release | x64
```

构建成功后产物位于：
- `bin\x64\{配置}\VisionAlgo.Native.dll` + `opencv_world455*.dll`
- `src\VisionPlatform\bin\{配置}\VisionPlatform.exe`（构建脚本会把算法 DLL 与 OpenCV 运行时复制过来）

### 4. 运行
直接运行 `src\VisionPlatform\bin\Debug\VisionPlatform.exe`，或在 VS2022 中将 `VisionPlatform` 设为启动项目后按 F5。无相机硬件时可用模拟相机 `SIM-0` 跑通完整流程。

### 5. 打开解决方案
双击 `VisionSystem.sln`（**唯一解决方案**）即可在 VS2022 中开发全部四个项目。

---

## 模块职责速览

| 层 | 项目 / 目录 | 职责 |
|---|---|---|
| 算法层 | `VisionAlgo.Native` | C++ / OpenCV 实现：预处理、Blob、清晰度、圆/直线拟合、模板匹配 |
| P/Invoke 边界 | `VisionPlatform/Native/VisionAlgo.cs` | 唯一接触 C++ 的地方，结构体一一映射、零拷贝传入、输出由 C# 分配 |
| 业务层 | `VisionPlatform/Core` | Logger / AppPaths / Json / OpResult / AppSettings（AppConfig 三段式配置 + ConfigStore）/ ImageStore |
|  | `VisionPlatform/Camera` | 相机接口与模拟/海康实现、相机管理 |
|  | `VisionPlatform/Communication` | TCP 客户端/服务端、Modbus TCP、`CommState`（通讯全局状态） |
|  | `VisionPlatform/Recipe` | 配方模型与 JSON 存储 |
|  | `VisionPlatform/Vision` | 检测配置、`InspectionEngine` 流程编排与 OK/NG 判定 |
| 显示层 | `VisionSystem.Controls` | 图像显示、几何图形、OpenCV 互操作 |
| 表现层 | `VisionPlatform/UI` | 登录、启动、主运行界面、系统设置及各功能窗体 |

> 界面不直接碰算法，算法不认识界面。中间仅靠 `Native/VisionAlgo.cs` 这一道 P/Invoke 边界隔离。

**一次检测的数据流**：相机 `GrabOne()` → `InspectionEngine.Run()`（预处理 → 各项算法 → 汇总判定）→ 结果以灰度图 + 几何图形叠加形式回显到 `ImageViewer`，并刷新 OK/NG 统计。

---

## 配置持久化（本地参数）

所有配置统一存放在 `%AppData%/VisionPlatform/appconfig.json`，由 `Core/AppSettings.cs` 的 `ConfigStore` 负责加载与保存：启动时 `Program.cs` 自动 `ConfigStore.Load()`，退出前 `MainForm` 自动 `ConfigStore.Save()`；任意子窗体保存后还会触发 `ConfigStore.Changed` 事件让主界面刷新状态。

配置按三类清晰分区，便于维护与个人化：

| 分区 | 类型 | 内容 |
|---|---|---|
| `System` 系统参数 | `SystemSettings` | 图片保存路径、保存格式、保留天数、检测后自动保存、仅存 NG、循环运行前要求通讯已连接 |
| `Algorithm` 算法参数 | `AlgorithmSettings` | 上次使用的配方名 `CurrentRecipeName` + 一份检测参数快照 `Inspection`，保证二次打开即恢复上次检测配置 |
| `Ui` 界面设置参数 | `UiSettings` | 主界面布局索引 `LayoutIndex`、各视图的相机绑定 `ViewBindings`（视图序号 → 相机 Id） |

相机绑定在用户切换时即时写入 `ViewBindings`，重启后主界面按保存的 `LayoutIndex` 与 `ViewBindings` 重建视图并恢复各自的相机绑定。

> 历史兼容：`ConfigStore.Load()` 在文件缺失或字段缺失时按默认值补齐，旧版仅含系统参数的配置也能正常升级到三段式结构。

---

## 文档索引

- [平台架构.md](docs/平台架构.md) —— 分层架构、技术栈、目录结构、模块职责、C++↔C# 调用规范、扩展指南
- [使用说明.md](docs/使用说明.md) —— 操作方式、集成到现有项目、OpenCV 对接、常见问题
- [API参考.md](docs/API参考.md) —— 控件库全部公开属性、方法、事件
- [遗留问题.md](docs/遗留问题.md) —— 已知问题、占位实现（如海康相机 SDK）、后续 TODO
- [ThirdParty/README.md](ThirdParty/README.md) —— 第三方依赖准备与引用方式

---

## 已知限制

- 海康相机 `HikCamera` 当前为占位实现（未安装 MVS SDK），需硬件时请参考 `ThirdParty/HikVision/README.md`。
- 模板匹配暂不具备旋转/尺度不变性。
- 登录使用硬编码账号；检测结果暂不持久化；无硬触发信号。
- 端到端运行验证尚未执行。

详细内容见 [遗留问题.md](docs/遗留问题.md)。
