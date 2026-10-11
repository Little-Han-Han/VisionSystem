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
- **相机管理**：统一相机接口 `ICamera`；**本机摄像头 `LocalCamera`**（USB / 笔记本内置摄像头，支持实时预览，经 C++ 层 OpenCV `VideoCapture` 接入，**零第三方依赖**）；模拟相机 `SimCamera` 跑通全流程；海康 `HikCamera` 适配器（占位，见遗留问题）
- **通讯**：TCP 客户端/服务端、`ModbusTcpClient`（自行实现 MBAP+PDU，支持 0x03/0x06/0x10）
- **配方**：`Recipe` + `RecipeStore`，JSON 存取、当前配方切换自动同步参数
- **算法**：**流程编排页 `FlowForm`**（VisionMaster 风格模块拖拽式：工具箱 → 画布节点 → 连线 → 绑定相机与显示视图 → 运行，支持多流程）＋ **形状匹配调试页 `AlgoForm`**（离线单图工作台，可打开本地图片或从相机取帧，无需相机即可调试）
- **系统设置**：图片保存路径、保存格式（PNG/JPEG/BMP/TIFF）、保留天数（自动清理过期图片）、检测后自动保存与仅存 NG、循环运行前是否要求通讯已连接；全部参数按「系统 / 算法 / 界面」三类持久化到本地 JSON（`%AppData%/VisionPlatform/appconfig.json`），重启自动加载，无需重新配置
- **主运行界面** `MainForm`：布局可调的多显示视图（1x1 / 1x2 / 2x1 / 2x2 / 3x2 / 3x3，最多 9 个）、**每个视图可单独绑定相机**、单帧采集与多相机同步采集、循环运行/暂停/停止、数据统计（总数/OK/NG/良率/节拍）、运行日志

#### 模块拖拽式流程编排（VisionMaster 风格）

`FlowForm` 是算法主入口（主界面工具栏「流程编排」按钮），原 `AlgoForm` 保留为「形状匹配调试」单图工作台。界面四区：**工具箱 | 流程画布 | 节点参数面板 | 底部流程管理 + 绑定**。

| 能力 | 说明 |
|---|---|
| 多流程 | 新建 / 重命名 / 删除 / 切换；删空自动补一条空流程 |
| 模块拖拽 | 从工具箱（**纯文字块，不用图标**）拖入画布生成节点；节点可拖动改位置 |
| 连线 | 节点间画有向连线表示执行顺序（单链约束）；选中节点/连线按 Delete 删除 |
| 流程绑定 | 每个流程绑定一个**相机**（`CameraId`）与主界面一个**显示视图**（`ViewIndex`） |
| 节点参数 | 选中节点后在参数面板编辑；本期接通**形状匹配**节点，其余模块为空态占位 |
| 运行 | 按连线顺序跑一遍（单帧）：图像源 → 算法节点 → 结果输出；结果回传主界面并按绑定视图叠加显示 |
| 持久化 | 流程 / 节点 / 连线 / 位置 / 参数 / 绑定全部写入 `appconfig.json` 的 `Flows` 段，重启恢复；旧配置自动补齐默认段 |

**运行期保护**：流程绑定的相机未打开、或绑定视图号超出当前布局时，给出可读中文提示并跳过显示（**不自动改写绑定**）；运行中关闭编排窗体时会先取消本次关闭、待运行收尾后自动关窗，避免并发访问已释放的原生算法上下文。

#### 本机摄像头接入

- 相机管理列表顺序为「本机 → 模拟 → 海康」，本机设备显示为 `[USB] 本机摄像头 n`
- 打开后可**连续采集实时预览**：采集端节流至约 30fps，显示端采用「覆盖式投递」（只保留最新帧、旧帧立即释放），UI 渲染慢时不会积压内存
- 无摄像头机器上列表仍有模拟相机兜底，刷新/打开不报错；设备被占用时给出可读失败原因
- 关闭相机时若采集线程仍阻塞在设备读，改为**后台延迟释放句柄**，不阻塞界面

### C++ 算法库（VisionAlgo.Native）
预处理、Blob 分析、清晰度（边缘/模板）、圆拟合、直线拟合、模板匹配（灰度归一化相关）、**形状匹配（基于边缘轮廓）**、**本机摄像头采集**（`algo/va_camera.h`，基于 OpenCV videoio）。导出为 x64 DLL，由 C# 通过唯一 P/Invoke 边界调用。

**形状匹配**（`algo/va_shape.h`）以边缘点的梯度方向为特征，较灰度相关匹配更抗光照变化与局部遮挡，支持：

| 能力 | 说明 |
|---|---|
| 角度搜索 | 指定角度区间与步长（度），可覆盖全周 360° |
| 缩放搜索 | 指定缩放区间与步长；步长为 0 时只按最小缩放搜索 |
| 图像金字塔 | 1~8 层，由粗到细搜索，粗层全局扫描 + 细层邻域精化 |
| 搜索 ROI | 限制「模板中心」可出现的范围，缩小范围可显著提速 |
| 多目标 | 最多返回 N 个目标，按得分排序并做非极大值抑制（重叠阈值可调） |
| 特征稀疏 | 训练时按网格做**空间稀疏化**：同格只留梯度最强点，可设「最多特征点数」与「最小间距」；点数越少越快，且分布均匀比按幅值抽取更稳 |
| 结果表格 | 结果以表格展示（序号 / X / Y / 角度 / 缩放 / 得分），**点列头即可排序** |
| 亚像素 | 最细层对位置（0.25 px）、角度与缩放再做联合精化 |
| 提前终止 | ①理论上界剪枝；②贪婪启发（可调 `greediness`） |
| 特征导出 | `va_get_shape_model_points` 导出边缘点与梯度方向，界面可作为独立图层显示 |

**速度优化**（C++ 侧三项叠加，已实测）：

1. **姿态预计算** —— 角度/缩放的旋转只按「姿态」算一次供全部位置复用，避免在「位置 × 角度 × 缩放」三重循环里反复做三角函数与旋转。
2. **提前终止** —— 理论上界（剩余点全满分也达不到阈值即放弃）+ 贪婪启发（强边缘平均分过低即放弃，`greediness` 控制强度，默认 0.9）。
3. **OpenMP + SSE2** —— 粗扫按位置、精化按候选并行；评分累积段用 SSE2 四路向量化。
   （采样地址发散无法向量化，故先用标量取回 4 个点的梯度，再做向量化算术。）

实测同一 320×240 合成图、2 个目标（真值 (80,70)/0°/1.0× 与 (220,160)/25°/1.2×）：

| 配置 | 优化前 | 优化后 | 提速 |
|---|---|---|---|
| Debug · 小搜索（21 姿态） | 7952 ms | **52 ms** | ≈152× |
| Debug · 中搜索（305 姿态） | 113179 ms | **607 ms** | ≈186× |
| Release · 小搜索 | 2252 ms | **10.7 ms** | ≈210× |
| Release · 中搜索 | 33735 ms | **131 ms** | ≈257× |

> 优化前金字塔层数因约束过严退化为 1（等于全分辨率暴力扫描），**这是此前最大的性能陷阱**，已修正为「最粗层模板 ≥8px、图像 ≥32px」。

**特征点稀疏化的收益**（Release，同一 305 姿态搜索，2 个目标）：

| 训练参数 | 实际特征点 | 耗时 | 结果 |
|---|---|---|---|
| 3000 点 / 间距 1.0（密） | 172 | 267 ms | 25° / 1.19× ✓ |
| 1500 点 / 间距 2.0（默认） | 86 | 138 ms | 25° / 1.19× ✓ |
| 600 点 / 间距 3.0 | 59 | 95 ms | 25° / 1.19× ✓ |
| 150 点 / 间距 6.0（稀） | 28 | **89 ms** | 25° / 1.19× ✓ |

稀疏化后精度不降反稳（特征点均匀分布，避免成团导致姿态抖动）。
配套改动：贪婪剪枝的 `minEval` 由固定 16 改为**按点数取 1/4（限 6~24）**，否则小模型要跑满全程才触发剪枝，稀疏化提速不明显。

> 耗时仍近似正比于「搜索位置数 × 角度数 × 缩放数」，界面会实时显示待评估姿态数；建议先用 ROI 缩小范围、再用较大步长粗定位。

**彩色输入**：算法库只接收 8bpp 灰度（形状匹配只依赖边缘的梯度方向，与颜色无关）。C# 侧 `GrayInput` 先判断像素格式再转换：

- 彩色图按 **Rec.601 亮度公式**转灰度：`Y = (114·B + 587·G + 299·R) / 1000`
- 32bpp / 16bpp / 48bpp / 64bpp 以及调色板非灰度的 8bpp 索引图 → 先经 GDI+ 归一化成 24bpp RGB 再取亮度
- 灰度且调色板为恒等的 8bpp 索引图 → 直接读取，零拷贝之外的转换开销也省掉
- 转换结果放入托管 `byte[]` 并 **pinned** 后传入，stride = width（连续无填充），用毕释放

这样算法库拿到的**永远是 channels=1 的连续灰度缓冲区**，彻底避免通道数误判。载入图像后界面会提示「彩色图（算法内部自动转灰度处理）」。

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
    │   │       ├── va_shape.h             形状匹配（边缘轮廓 / 角度 / 缩放 / 金字塔 / ROI）
    │   │       └── va_measure.h           测量（清晰度等）
    │   ├── src/
    │   │   ├── vision_algo.cpp            导出层：句柄校验 + 转发
    │   │   └── algo/                      算法实现
    │   │       ├── algo_preprocess.cpp
    │   │       ├── algo_blob.cpp
    │   │       ├── algo_fit.cpp
    │   │       ├── algo_match.cpp
    │   │       ├── algo_shape.cpp
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
| P/Invoke 边界 | `VisionPlatform/Native/VisionAlgo.cs`、`Native/NativeCamera.cs` | 唯一接触 C++ 的地方，结构体一一映射、零拷贝传入、输出由 C# 分配（相机帧复用 `VA_Image`，`channels=3` BGR，`stride` 如实传递） |
| 业务层 | `VisionPlatform/Core` | Logger / AppPaths / Json / OpResult / AppSettings（AppConfig 三段式配置 + ConfigStore）/ ImageStore |
|  | `VisionPlatform/Camera` | 相机接口与**本机摄像头 `LocalCamera`** / 模拟 / 海康实现、相机管理（`USB` 前缀分发） |
|  | `VisionPlatform/Flow` | 流程模型（节点/连线/参数）、模块目录 `FlowModule`、单链执行器 `FlowRunner`、结果回传通道 `FlowOutputChannel` |
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
| `Flows` 流程参数 | `FlowSettings` | 流程列表（节点 / 连线 / 画布位置 / 节点参数）+ 每个流程绑定的相机 `CameraId` 与主界面显示视图 `ViewIndex` |

相机绑定在用户切换时即时写入 `ViewBindings`，重启后主界面按保存的 `LayoutIndex` 与 `ViewBindings` 重建视图并恢复各自的相机绑定。

> 历史兼容：`ConfigStore.Load()` 在文件缺失或字段缺失时按默认值补齐，旧版仅含系统参数的配置也能正常升级到三段式结构。

---

## 文档索引

- [平台架构.md](docs/平台架构.md) —— 分层架构、技术栈、目录结构、模块职责、C++↔C# 调用规范、扩展指南
- [使用说明.md](docs/使用说明.md) —— 操作方式、集成到现有项目、OpenCV 对接、常见问题
- [API参考.md](docs/API参考.md) —— 控件库全部公开属性、方法、事件
- [遗留问题.md](docs/遗留问题.md) —— 已知问题、占位实现（如海康相机 SDK）、后续 TODO
- [形状匹配参考资料.md](docs/形状匹配参考资料.md) —— 形状匹配精选论文/开源资料（Shape Context、HALCON 边缘梯度匹配原理、OpenCV matchShapes / modules/shape / matchTemplate、ORB）及其在 `algo_shape.cpp` 中的对照
- [PRD-摄像头与流程编排.md](docs/PRD-摄像头与流程编排.md) —— 本机摄像头接入 + 流程编排的增量 PRD（需求池 FR-CAM/FR-FLOW、验收标准、界面线框）
- [架构-摄像头与流程编排.md](docs/架构-摄像头与流程编排.md) —— 上述功能的增量设计（文件清单、数据结构与接口、时序图、任务分解、共享知识）
- [测试报告-摄像头与流程编排.md](docs/测试报告-摄像头与流程编排.md) —— 原生实证 20 用例、加固回归、收尾确认与需人工验证清单
- [ThirdParty/README.md](ThirdParty/README.md) —— 第三方依赖准备与引用方式

---

## 已知限制

- 海康相机 `HikCamera` 当前为占位实现（未安装 MVS SDK），需硬件时请参考 `ThirdParty/HikVision/README.md`。
- 灰度归一化相关的**模板匹配**（`va_match.h`）只支持平移，不具备旋转/尺度不变性；需要旋转/缩放时请使用**形状匹配**（`va_shape.h`）。
- 登录使用硬编码账号；检测结果暂不持久化；无硬触发信号。
- **本机摄像头枚举为探测式**（OpenCV 无跨平台枚举 API，逐个尝试打开 0..7）：刷新列表有数百毫秒开销，且拿不到设备友好名，统一显示「本机摄像头 n」。
- C++ 侧 `VideoCapture::read` 是**阻塞读且无超时**：设备异常（假死）时，停止采集后可能短时间内无法再次启动（界面会给中文提示；关闭相机再重新打开即可恢复）。
- 流程编排为**单链有向**、点「运行」只跑一遍（单帧）；分支/并发与连续运行属后续扩展。
- 端到端运行验证尚未执行（自动化已覆盖原生链路与构建，界面交互需人工确认，见测试报告第 5 节）。

详细内容见 [遗留问题.md](docs/遗留问题.md)。
