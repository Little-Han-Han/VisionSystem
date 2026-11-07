# VisionSystem — 工业机器视觉软件

一套完整的工业视觉检测软件：用 **C++ / OpenCV** 编写视觉算法，用 **C# / WinForms (.NET Framework 4.8)** 编写软件界面，通过 P/Invoke 调用 C++ 算法 DLL。整套代码包含在一个解决方案 `VisionSystem.sln` 内，共 4 个项目。

- 目标框架：**.NET Framework 4.8**
- 算法引擎：**C++ / OpenCV 4.5.5**
- 开发工具：**Visual Studio 2022 Community**（`D:\Program Files\Microsoft Visual Studio\2022\Community`）
- 平台：**全部 x64**（算法库为 x64，主程序 `PlatformTarget=x64`）
- 第三方依赖：**零 NuGet 包**（JSON 用 `DataContractJsonSerializer`，Modbus/TCP 自行实现），外部 DLL 统一收纳在 `ThirdParty/`

---

## 整体软件架构

四层结构，自底向上：

```
┌─ 算法层    VisionAlgo.Native  (C++/OpenCV) ── 预处理 / 匹配 / 测量 / 检测 / OCR / 相机采集
│           导出为 x64 DLL，按 va_ 前缀 C 接口暴露
├─ P/Invoke 边界  VisionPlatform/Native/VisionAlgo.cs、NativeCamera.cs
│           （界面与算法之间唯一的接触面：结构体一一映射、零拷贝传入、输出由 C# 分配）
├─ 业务层    VisionPlatform  (C#) ── 配置 / 相机 / 流程 / 通讯 / 配方 / 检测引擎
└─ 表现层    VisionPlatform/UI + VisionSystem.Controls ── 主界面、各功能窗体、图像显示控件库
```

**核心约束**：界面不直接碰算法，算法不认识界面，中间仅靠 `Native/VisionAlgo.cs` 这一道 P/Invoke 边界隔离。

**多流程并行**：`FlowRunManager` 为每条流程维护一个 `FlowRunSlot`（独立线程 + 独立原生算法上下文 `va::Context`），「全部启动」并行跑所有流程；相机按 `CameraLease` 独占（默认严格独占，避免并发取帧打崩厂商 SDK），同相机取帧由 `CameraGrabGate` 串行化。

**一次检测的数据流**：相机 `GrabOne()` → `FlowRunner` 按节点链执行（图像源 → 各算法节点 → 汇总判定）→ 结果以灰度图 + 几何图形叠加形式回显到 `ImageViewer`，并刷新 OK/NG 统计。

### 模块职责

| 层 | 项目 / 目录 | 职责 |
|---|---|---|
| 算法层 | `VisionAlgo.Native` | C++ / OpenCV：预处理、Blob、清晰度、圆/直线拟合、模板/形状匹配、目标检测、OCR、本机相机采集 |
| P/Invoke 边界 | `VisionPlatform/Native/VisionAlgo.cs`、`Native/NativeCamera.cs` | 唯一接触 C++ 处；结构体映射、零拷贝传入、相机帧经 `VA_Image`（BGR，`stride` 如实传递） |
| 业务层 | `VisionPlatform/Core` | Logger / AppPaths / Json / OpResult / AppSettings（AppConfig 三段式 + ConfigStore）/ ImageStore |
|  | `VisionPlatform/Camera` | 相机接口与**本机摄像头 `LocalCamera`** / 模拟 `SimCamera` / 海康 `HikCamera`（占位）/ CameraManager |
|  | `VisionPlatform/Flow` | 流程模型、模块目录 `FlowModule`、单链执行器 `FlowRunner`、并行管理器 `FlowRunManager`、结果通道 `FlowOutputChannel` |
|  | `VisionPlatform/Communication` | TCP 客户端/服务端、Modbus TCP、`CommState` |
|  | `VisionPlatform/Recipe` | 配方模型与 JSON 存储 |
|  | `VisionPlatform/Vision` | 检测配置、`InspectionEngine` |
| 显示层 | `VisionSystem.Controls` | 图像显示、几何图形、OpenCV 互操作 |
| 表现层 | `VisionPlatform/UI` | 登录、主运行界面、系统设置及各功能窗体 |

---

## 已实现功能

- **登录 / 启动加载**：`LoginForm` + `SplashForm`
- **相机管理**：统一相机接口 `ICamera`；**本机摄像头 `LocalCamera`**（USB / 笔记本内置，OpenCV `VideoCapture` 接入，**零第三方依赖**，支持实时预览）；模拟相机 `SimCamera` 跑通全流程；海康 `HikCamera` 为占位实现（见尚未完善）
- **主运行界面 `MainForm`**：布局可调的多显示视图（1x1 / 1x2 / 2x1 / 2x2 / 3x2 / 3x3，最多 9 个），每个视图可单独绑定相机；单帧 / 同步采集；**多流程循环运行**（全部启动 / 暂停 / 停止）；数据统计（总数 / OK / NG / 良率 / 节拍）；各流程统计表（含每条流程实时状态：运行中 / 已中止 / 已停止 等）
- **流程编排 `FlowForm`**（VisionMaster 风格）：多流程（增删改切）、工具箱拖拽生成节点、节点间有向连线、每流程绑定相机与显示视图、运行回显；另保留 `AlgoForm` 作为离线单图算法调试工作台
- **通讯**：TCP 客户端/服务端、`ModbusTcpClient`（自行实现 MBAP+PDU，支持 0x03/0x06/0x10 及多数据类型）
- **配方**：`Recipe` + `RecipeStore`，JSON 存取、当前配方切换自动同步参数
- **系统设置**：图片保存路径 / 格式（PNG/JPEG/BMP/TIFF）/ 保留天数（自动清理）/ 检测后自动保存与仅存 NG / 循环运行前是否要求通讯已连接
- **配置持久化**：系统 / 算法 / 界面 / 流程 四类参数落到本地 JSON（`%AppData%/VisionPlatform/appconfig.json`），重启自动加载
- **图像显示控件库 `VisionSystem.Controls`**：双缓冲、大图流畅缩放平移、几何图形（圆/矩形/旋转矩形/圆环）编辑、HUD 坐标与像素值、与 OpenCV `Mat` 零拷贝互通

---

## 已有算法清单

流程节点（`FlowNodeType`）对应的算法，逐个简要说明；基础拟合类从简。

**图像源 / 输出**
- **图像源 ImageSource**：相机取帧或本地图片，流程起点。
- **结果输出 ResultOutput**：汇总 OK/NG 判定与结果发布，流程终点。

**预处理与滤波**
- **预处理 Preprocess**：灰度转换、高斯模糊、二值化、形态学。
- **图像滤波 ImageFilter**：平滑 / 锐化 + 可选形态学，支持灰度与彩色两条链路。

**匹配与定位**
- **形状匹配 ShapeMatch**：基于边缘轮廓，支持角度 / 缩放搜索、图像金字塔、搜索 ROI、多目标与亚像素，抗光照变化与局部遮挡。
- **模板匹配 TemplateMatch**：NCC 灰度归一化匹配，仅支持平移（需旋转/缩放请用形状匹配）。
- **位置修正 PositionCorrect**：由匹配结果输出 dX/dY/dAngle/dScale，修正后续节点坐标。

**测量与拟合**
- **直线卡尺 LineCaliper**：投影找边 + 直线拟合 + 直线度 RMS。
- **圆卡尺 CircleCaliper**：径向投影找边 + 圆拟合 + 圆度 RMS。
- **圆拟合 FitCircle / 直线拟合 FitLine**：基础几何拟合。
- **Blob 分析 Blob**：连通域统计（面积 / 质心 / 外接框等）。
- **清晰度 Sharpness**：边缘 / 模板类清晰度评估。

**颜色 / 深度学习 / 字符**
- **颜色提取 ColorExtractor**：BGR / HSV 阈值分割 → 连通域统计。
- **目标识别 ObjectDetect**：OpenVINO YOLO 深度学习目标检测（类别 / 分数 / 坐标）。
- **字符识别 Ocr**：传统算法，字符分割 + 模板匹配（模板为「一个字符一张图、文件名即标签」）。

**通讯节点**
- **发送数据 SendData / 接收数据 ReceiveData**：按名称绑定通讯链路，发送文本 / 写 Modbus 寄存器，或读取文本 / 读保持寄存器。

---

## 尚未完善 / 已知限制

**P0 — 需补充环境才能完成**
- **海康相机 `HikCamera` 为占位实现**：已完整实现 `ICamera` 契约与 SDK 探测逻辑，但未安装 MVS SDK，真实取流未接（源码以 `// TODO(SDK):` 标注）。可用模拟相机 `SIM-0` 跑通全链路。

**P1 — 功能不完整**
- **登录为内置账号表**，生产环境应换数据库 / LDAP / 域认证；**未做权限细分**（操作员/管理员角色未与界面功能绑定）。
- **相机仅支持软触发**，无硬触发 / IO 接口，外部触发信号接不进来。
- **配方不含相机参数**：`Recipe` 只有 `CameraId`，曝光、增益、触发模式等未纳入。
- **检测结果统计未落盘**：图像存证（保存路径 / 格式 / 保留天数 / 自动保存 / 仅存 NG）已完成，但 OK/NG **统计结果本身**仍未持久化（无数据库或 CSV 报表）。
- **通讯未与检测结果联动**：TCP/Modbus 目前手动收发，未实现「检测完成后自动推送结果 / 写寄存器」。

**P2 — 工程与运行限制**
- **流程为单链有向、单帧执行**：单条流程内不支持分支/并发；「全部启动」可在主界面并行调度多条流程（各流程独立线程）。分支/并发属后续扩展。
- **本机摄像头枚举为探测式**（OpenCV 无跨平台枚举 API，逐个尝试打开 0..7）：刷新列表有数百毫秒开销，且拿不到设备友好名（统一显示「本机摄像头 n」）。
- C++ 侧 `VideoCapture::read` **阻塞读且无超时**：设备假死时停止采集后可能短时无法再次启动（关闭相机重开即可恢复）。
- **端到端运行验证尚未自动化执行**：自动化已覆盖原生链路与构建，界面交互需人工确认（无相机硬件时可用 `SIM-0` + 本地图片跑通）。
- 登录使用硬编码账号；部分较新算法节点的参数面板可能仍需打磨。

详细内容见 [遗留问题.md](docs/遗留问题.md)。

---

## 目录结构

```
VisionSystem/                              唯一解决方案根目录
├── VisionSystem.sln                       单一解决方案（4 个项目 + Solution Items）
├── build.bat                              一键构建（含代理变量清理）
├── README.md                              本文件
├── ThirdParty/                            所有外部依赖，按用途分次级目录
│   ├── OpenCV/                            OpenCV 4.5.5（include / lib/x64 已入库，bin/x64 需自备）
│   ├── OpenVino2025.4.1/                  OpenVINO CPU 推理运行时
│   ├── SQLite/                            日志库原生 DLL
│   └── HikVision/                         海康 MVS SDK（当前为空）
├── bin/x64/{Debug,Release}/               算法 DLL 与 OpenCV 运行时（构建产物）
├── docs/                                  文档（入口：docs/README.md 知识图谱）
│   ├── README.md                          知识图谱：概念图 / 唯一权威出处 / 文档地图
│   ├── 平台架构.md                        架构、分层、模块职责（待更新）
│   ├── API参考.md                         控件库完整 API 清单（待更新）
│   ├── 遗留问题.md                        已知问题、TODO、占位实现（待更新）
│   ├── 需求/增量需求总览.md                5 次增量 PRD 合并
│   ├── 架构/增量架构设计.md                6 份架构设计合并 + 公共约定唯一出处
│   ├── 参考资料/                          参考资料汇编 + 使用说明
│   └── 图表/                              6 份 Mermaid 类图 / 时序图
└── src/
    ├── VisionAlgo.Native/                 【C++ / OpenCV】算法 DLL
    │   ├── include/
    │   │   ├── vision_algo.h              聚合头
    │   │   └── algo/                      算法头文件（preprocess / blob / fit / match / shape / measure / detect / ocr / filter / color / camera）
    │   ├── src/
    │   │   ├── vision_algo.cpp            导出层：句柄校验 + 转发
    │   │   └── algo/                      算法实现
    │   ├── Common/                        公共：导出宏/错误码/类型/生命周期/内部工具
    │   ├── VisionAlgo.Native.vcxproj
    │   └── VisionAlgo.Native.vcxproj.filters
    ├── VisionSystem.Controls/              【C#】图像显示控件库
    │   ├── ImageViewer.cs                  核心控件
    │   ├── Geometry/                       IGeometryShape / ShapeHandle / 各形状 / ShapeCollection
    │   ├── Interop/                        CvInterop（OpenCV 数据互操作）
    │   └── Rendering/                      Viewport（坐标变换）/ RenderOptions
    └── VisionPlatform/                     【C#】工业视觉主程序
        ├── Program.cs
        ├── Core/                          Logger / AppPaths / Json / OpResult /
        │                                 AppSettings（AppConfig 三段式 + ConfigStore）/ ImageStore
        ├── Native/                        VisionAlgo（P/Invoke 封装）/ NativeCamera
        ├── Camera/                        ICamera / LocalCamera / SimCamera / HikCamera / CameraManager
        ├── Communication/                 TcpClientSession / TcpServerSession / ModbusTcpClient / CommState
        ├── Recipe/                        Recipe / RecipeStore
        ├── Vision/                        InspectionConfig / InspectionResult / InspectionEngine
        ├── Models/                        User
        └── UI/                            MainForm / CameraForm / AlgoForm / CommForm /
                                           RecipeForm / SettingsForm / LoginForm / SplashForm / FlowForm
                                           （各自 .cs + .Designer.cs）
```

> **说明**：所有窗体均采用 **Designer 模式**，拆分为 `XxxForm.cs`（业务逻辑）与 `XxxForm.Designer.cs`（布局）。C++ 项目通过 `.vcxproj.filters` 维护解决方案资源管理器中的文件夹层级。

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

（只跑 Release 时只需 `opencv_world455.dll`；调试需要 `455d.dll`。）

> `ThirdParty/` 按用途分目录：`OpenCV/`（4.5.5 头文件 + 导入库）、`OpenVino2025.4.1/runtime/`（CPU 推理运行时）、
> `SQLite/`（日志库原生 DLL）、`HikVision/`（海康 MVS SDK，当前为空）、`Communication/`（当前无第三方依赖）。
> 工程统一用 `$(ProjectDir)..\..\ThirdParty\...` 相对路径引用，不依赖系统 PATH；未安装 MVS SDK 时
> 海康相机为占位实现，可用模拟相机 `SIM-0` 跑通全流程。

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

## 文档索引

- [知识图谱 docs/README.md](docs/README.md) —— **先看这里**：概念关系图、「概念 → 唯一权威出处」表、旧文档→新章节映射、图表索引
- [增量需求总览.md](docs/需求/增量需求总览.md) —— 5 次增量 PRD 合并：目标、需求编号总表、验收口径
- [增量架构设计.md](docs/架构/增量架构设计.md) —— 6 份架构设计合并：关键决策、接口要点、落地红线；§7 是跨增量公共约定的唯一出处
- [参考资料汇编.md](docs/参考资料/参考资料汇编.md) —— 形状匹配论文/开源资料 + VisionPro 界面参考图实测
- [使用说明.md](docs/参考资料/使用说明.md) —— 操作方式、集成到现有项目、OpenCV 对接、常见问题
- [平台架构.md](docs/平台架构.md) —— 分层架构、模块职责、C++↔C# 调用规范（**待更新：架构复用率改造**）
- [API参考.md](docs/API参考.md) —— 控件库全部公开属性、方法、事件（**待更新**）
- [遗留问题.md](docs/遗留问题.md) —— 已知问题、占位实现（如海康相机 SDK）、后续 TODO（**待更新**）
- [快速开始 → 准备 OpenCV 运行时 DLL](#2-准备-opencv-运行时-dll) —— 第三方依赖准备与引用方式
