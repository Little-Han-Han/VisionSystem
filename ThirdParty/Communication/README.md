# Communication 协议插件目录

把第三方协议驱动放在这里，构建时由 `VisionPlatform.csproj` 的 PostBuild 复制到
`bin\x64\<配置>\Communication\`，运行时由 `CommProtocolRegistry` 自动加载。

## 插件要求

- 目标框架：**.NET Framework 4.8**
- 只引用 `VisionPlatform.Framework.dll`（不要引用宿主或 WinForms）
- 实现 `VisionPlatform.Framework.Communication.ICommProtocolFactory`
- 程序集内每个工厂的 `Kind` 必须唯一（例如 `OpcUa`）

## OPC UA

当前仓库未内置 OPC UA SDK。需要时把 OPC UA 客户端 SDK 的 DLL 与适配器 DLL
一起放到本目录（例如 `Opc.Ua.Client.dll`、`VisionPlatform.Comm.OpcUa.dll`），
适配器实现 `ICommProtocolFactory`，`Kind = "OpcUa"`，点位地址使用 NodeId 字符串
（`ns=2;s=Line1.Ready`）。宿主核心无需修改。

> 若没有 SDK，端点会显示「协议插件未安装（OpcUa）」，不影响 TCP / Modbus。
