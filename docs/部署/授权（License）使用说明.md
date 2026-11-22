# 授权（License）使用说明

> **当前状态：已启用强制校验（`Enforce`）**。没有有效许可证时软件**无法启动**，会弹窗告知原因与本机机器码后退出。
>
> 验签实现不在本仓库：引用 `LicenseTool` 仓库的 **`VisionLicense.Contract.dll`**（签发端与客户端共用同一份实现，避免两边算法漂移）。
> 契约与签发工具见 `..\LicenseTool\README.md`。

---

## 1. 许可证放哪里

```
<程序目录>\License\
    license.lic         ← 正式许可证（Contract 只认这个名字）
    licence.lic         ← 旧拼写也可以：启动时自动复制成 license.lic（原文件保留）
    licence_30days.lic  ← 试用证；换证时直接改名/复制即可
    .licstate           ← 时间回拨状态（Contract 维护，勿删；删了只影响回拨检测）
```

- 目录在启动时自动创建；`publish.bat` 也会在发布包里建好空目录。
- 换证 = 覆盖 `license.lic`，重启软件。
- **升级只换 exe/DLL/thirdparty，`License\` 目录天然保留。**

---

## 2. 启动时的行为

```
读取 License\license.lic
  ├─ 不存在 → 弹窗「未找到许可证」+ 机器码 → 退出
  ├─ FirstUse 未激活 → 自动激活（写第 4 段绑定本机）→ 继续校验
  ├─ 签名/产品/机器码/有效期 任一不符 → 弹窗「原因 + 机器码」→ 退出
  └─ 有效 → 正常启动，日志记录「授权有效 + 功能位」
```

弹窗只给**原因 + 本机机器码 + 许可证路径**，**没有「导入许可证」按钮** ——
换证属于商务动作，避免现场自行塞文件绕过。

> 现场唯一的自助能力：`VisionPlatform.exe --license` —— 弹只读信息框（当前授权状态 / 编号 / 客户 / 到期 / 功能位 / 机器码），不启动软件。

---

## 3. 三种模式（`Config\appconfig.json`）

```jsonc
{ "System": { "LicenseMode": "Enforce" } }   // Enforce（默认）| Warn | Off
```

| 模式 | 行为 | 用在哪 |
|---|---|---|
| `Enforce`（默认） | 不可用即弹窗并退出 | 正式发版 |
| `Warn` | 校验 + 记日志，不拦截 | 排障：怀疑是授权导致起不来时临时切换 |
| `Off` | 不拦截（仍读许可证用于功能位判断） | 内部开发机 |

> 字段缺失或写错一律按 `Enforce` 处理（宁严不松）。

---

## 4. 绑定方式与机器码

签发工具支持两种绑定（`LicenseInfo.BindMode`）：

| 模式 | 行为 | 现场体验 |
|---|---|---|
| **FirstUse**（当前两个证都是） | 签一张通用许可；**首次启动自动绑定本机**，绑定信息写进 `license.lic` 第 4 段 | 文件丢进去就能用；拷到第二台机器 → `MachineMismatch` |
| Machine | 签发时就绑定申请码 | 需先取机器码报给供应商 |

- 机器码：5 项指纹（主板 / CPU / 卷序列号 / MachineGuid / 物理网卡 MAC），
  取 `SHA256` 短哈希后按 **≥3/5** 匹配（`ProductCatalog.DefaultFingerprintThreshold`）。
- 换一张网卡或换硬盘不会锁死；整机更换必失败。
- 查看机器码：启动失败弹窗 / `--license` / 日志（`Logs\logs.db` 搜"机器码"）。

---

## 5. 功能位（商业分级）

`features` 字段决定模块可用性，常量在 `ProductCatalog.FeatureKeys`：

| key | 中文 | 说明 |
|---|---|---|
| `classic` | 传统算法 | **永远包含**，不做判断 |
| `deep` | 深度学习 | 许可证不含此项时：**工具箱里「深度学习」整个分类不显示**，节点拖不出来 |

- 判断入口统一：`AppContext.CanUseDeepLearning` → `AppContext.HasFeature("deep")`
- `AppContext.License` 为 `null`（`LicenseMode=Off` 或没读到证）时**不限制**，开发机不受影响
- 当前两个证都含 `classic + deep`；要验证限制效果，用 LicenseTool 签一张只勾「传统算法」的证

> 只做界面级限制（节点不在树里）。**已存在流程里的深度学习节点不会被删**，
> 打开旧流程仍正常显示 —— 避免换证导致现场方案丢节点。

---

## 6. 公钥

`src\VisionPlatform\Core\LicensePublicKey.cs` 里的 `Value` 常量：

```csharp
public const string Value = "RUNTMSAAAACupZ0f2S42c...";   // 来自 LicenseTool keys\vendor_public.txt 第 2 行
```

- 公钥不是秘密，随客户端发布；**私钥只在发证机**。
- 换密钥 = 改这里 + 重新发版，**存量客户必须全部重签**（老证会验不过）。

---

## 7. 依赖与部署

| 项 | 说明 |
|---|---|
| 引用 | `VisionLicense.Contract.dll`（放 `ThirdParty\License\`，csproj `Private=true` 自动拷到输出目录） |
| 源码 | 在 `LicenseTool\src\VisionLicense.Contract\`；改验签逻辑只改那边，再拷 DLL 回来 |
| 发布包 | `publish.bat` 的 `*.dll` 通配已包含它；`License\` 空目录也会建好 |
| 依赖 | 纯托管，无 NuGet、无原生库 |

> 这也回答了「License 放程序里还是独立库」：**独立库 + 引用 DLL**。
> 签发端与客户端必须共用同一份实现，否则字段/算法一改就出现"签得出、验不过"。

---

## 8. 出证流程（LicenseTool 侧）

```text
① 打开 LicenseAdmin → 发证页
② 客户名 / 版本（Standard/Pro/Trial）/ 有效期（永久/1年/3年/30天）
③ 绑定方式：FirstUse（推荐，现场免申请码）或 Machine（要先拿机器码）
④ 功能模块：勾「传统算法」（必选）、「深度学习」（按合同）
⑤ 生成 → 另存 .lic → 发给客户
⑥ 客户把文件放进 <程序目录>\License\（改名 license.lic），重启即生效
```

续期：同一客户重新签发一张，覆盖 `license.lic`（FirstUse 模式会重新绑定一次本机）。

---

## 9. 排障

| 现象 | 原因 | 处理 |
|---|---|---|
| 弹窗「未找到许可证」 | 文件不在 `License\`，或名字不是 `license.lic` / `licence.lic` | 放进目录改名；看日志里的"许可证文件"完整路径 |
| 弹窗「签名无效」 | 文件被改过 / 公钥不是它对应的那把 | 用当前密钥重签；或换回匹配的 `LicensePublicKey` |
| 弹窗「与本机不匹配」 | 证是别的机器绑的（FirstUse 已绑定） | 用本机机器码重新签发 |
| 弹窗「已过期」 | 到 30 天/约定日期 | 续签 |
| 弹窗「系统时间异常」 | 检测到时间回拨超宽限 | 校正系统时间 |
| 换证后仍报旧状态 | 进程没重启 | 关闭软件重新打开 |
| 想先排除授权因素 | — | `Config` 里临时 `LicenseMode=Warn`，看日志 |
