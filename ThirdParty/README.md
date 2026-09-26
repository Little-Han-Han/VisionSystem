# ThirdParty — 第三方依赖

所有外部依赖按**用途**放在本目录的次级文件夹中，项目统一通过**相对路径**引用，
不使用绝对路径、不依赖系统 PATH，保证仓库拷到别的机器仍可直接构建。

```
ThirdParty/
├── OpenCV/         OpenCV 4.5.5（C++ 算法库依赖）
├── HikVision/      海康 MVS SDK（当前为空，见其 README）
└── Communication/  通讯类第三方库（当前无依赖，自行实现协议）
```

---

## OpenCV（必需）

算法库 `VisionAlgo.Native` 依赖 OpenCV 4.5.5。

仓库中已包含：
- `OpenCV/include/` —— 头文件（约 6.6 MB）
- `OpenCV/lib/x64/` —— `opencv_world455.lib`、`opencv_world455d.lib`（约 6.5 MB）

**未包含**（因为体积超过 GitHub 单文件 100MB 上限）：
- `OpenCV/bin/x64/opencv_world455.dll`（约 61 MB）
- `OpenCV/bin/x64/opencv_world455d.dll`（约 122 MB）

### 准备运行时 DLL

从本机已有的 OpenCV 复制即可（或到 opencv.org 下载 4.5.5 Windows 包）：

```bat
set OPENCV=D:\Users\admin\Downloads\opencv\build
xcopy /Y "%OPENCV%\x64\vc15\bin\opencv_world455.dll"   ThirdParty\OpenCV\bin\x64\
xcopy /Y "%OPENCV%\x64\vc15\bin\opencv_world455d.dll"  ThirdParty\OpenCV\bin\x64\
```

构建脚本会自动把对应配置的 DLL 复制到输出目录，无需手动放到 exe 旁边。

> 只跑 Release 时只准备 `opencv_world455.dll` 即可；调试（Debug）需要 `455d.dll`。

---

## HikVision（可选）

未安装 MVS SDK 时，相机模块的 `HikCamera` 会给出明确提示，
程序仍可用模拟相机 `SIM-0` 跑通全流程。接入步骤见 `HikVision/README.md`。

---

## Communication（可选）

通讯模块（TCP / Modbus TCP）为自行实现，当前**无第三方依赖**。
若后续引入 NModbus 等库，请把 DLL 放到本目录并在 csproj 中用相对路径引用。
