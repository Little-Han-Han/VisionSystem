# ThirdParty / HikVision

存放海康威视（Hikrobot）工业相机的 SDK 程序集。

## 当前状态

**此目录为空** —— 本机尚未安装海康 MVS SDK，因此 `Camera/HikCamera.cs` 目前是占位实现
（带 `HikSdk` 探测逻辑，检测到 SDK 缺失时会给出明确提示）。

## 接入步骤

1. 安装海康 MVS（官网 `hikrobotics.com` → 服务支持 → 下载 → 机器视觉工业相机 SDK），
   默认安装到 `C:\Program Files (x86)\MVS`
2. 把以下文件复制到**本目录**：
   ```
   MVS\Development\DotNet\MvCameraControl.Net.dll      （托管封装，必需）
   MVS\Runtime\Win64\MvCameraControl.dll               （原生库，运行时必需）
   MVS\Runtime\Win64\MvCameraControl.dll               → 也可直接放到主程序输出目录
   ```
3. 在 `src/VisionPlatform/VisionPlatform.csproj` 中添加引用，指向 **本目录下的相对路径**：
   ```xml
   <Reference Include="MvCameraControl.Net">
     <HintPath>..\..\ThirdParty\HikVision\MvCameraControl.Net.dll</HintPath>
   </Reference>
   ```
4. 把 `Camera/HikCamera.cs` 中 4 处 `// TODO(SDK):` 分支替换为真实调用：
   ```
   MV_CC_EnumDevices → MV_CC_CreateHandle → MV_CC_OpenDevice
   → MV_CC_SetEnumValue(TriggerMode) → MV_CC_StartGrabbing
   → 取流线程 MV_CC_GetOneFrameTimeout → MV_CC_StopGrabbing → MV_CC_CloseDevice
   ```
5. 把 `MvCameraControl.dll`（原生）复制到主程序输出目录，否则运行时会报
   `DllNotFoundException`。

## 约定

- **所有第三方 DLL 一律放 ThirdParty 下对应子目录**，项目通过相对路径引用，
  不使用绝对路径、不依赖系统 PATH，保证整个仓库拷到别的机器仍可直接构建。
- 原生 DLL（非 .NET 程序集）需要随主程序输出，通过 `PostBuildEvent` 复制。
