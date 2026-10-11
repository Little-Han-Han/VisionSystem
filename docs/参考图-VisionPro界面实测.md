# 参考图（流程图参考图.png）实测结果

> 本文件的价值在于**可追溯**：每一项都标注了来源（像素测量 / OCR 文本 / 推断）。
> 主理人的模型**无法直接看图**，所以这里不使用任何"看起来像"的描述。

- 图片：`C:\Users\admin\Desktop\VisionSystem\流程图参考图.png`
- 尺寸：**1920 × 1159**（宽高比 1.657）
- 测量脚本：`tools/analyze_ref.py`、`tools/analyze_ref2.py`（Python + Pillow，只做统计，不做识别）
- 文字识别：`tools/ocr_ref.ps1` → `tools/ocr_ref.txt`（Windows 内置 OCR，WinRT，语言 `zh-Hans-CN`）

---

## 一、像素测量（来源：脚本统计，可信）

### 1.1 整体配色 —— 是**浅色界面**，不是深色

| 颜色 | 占比 | 说明 |
|---|---|---|
| `#F0F2F5` | 31.76% | 主背景（浅灰） |
| `#DFE2E6` | 11.19% | 次要背景 / 分隔 |
| `#BCC4CC` | 1.44% | 边框灰 |
| `#1D1D1D` / `#1C1C1C` / `#1B1B1B` 等 | 各约 0.6% | 深色块（画面/图标/文字） |

结论：参考图是**亮色主题**。本项目 `UiTheme` 当前是深/浅哪一套，需要据此对齐。

### 1.2 分区边界（亮度突变检测）

- 纵向分隔带 x ≈ **9, 279, 472, 657, 787, 855, 1017, 1206, 1631**
- 横向分隔带 y ≈ **20, 110, 170, 215, 290, 320, 812, 885, 915, 980**

### 1.3 深色大块（粗粒度分区图读出）

约 **x 760..1640，y 176..792**（880 × 616，宽高比约 1.43 ≈ 4:3）。
这块在整幅图里是唯一的连续深色区域，形态上像一块**图像显示区**。

### 1.4 蓝色高亮聚集区（脚本统计 top）

- `x 40..240, y 280..360` —— 左侧区域最密集的一片蓝色
- `x 360..480, y 80..120`
- `x 240..480, y 960..1000`
- `x 280..480, y 1080..1120`

蓝色通常对应**选中态 / 可点击链接**，集中在左侧竖栏和底部区域。

### 1.5 底部区域内容密度

从 y=909 起逐行统计"非背景像素占比"：

- `y=909` → **72.3%**（一条强分隔线/标题条）
- `y=919..959` → 0.6%~13.9%（稀疏）
- `y=979..1089` → **48%~60% 连续高位**（内容密集区，高约 110px）
- `y=1119..1129` → 30.6%（最底部还有一行）
- `y=1139` 以后 → 接近 0

---

## 二、OCR 实测文本（来源：Windows OCR，可信；个别字符有误码）

原始输出见 `tools/ocr_ref.txt`。以下按语义整理，**误码已按上下文还原并在括号内标注**。

### 2.1 标题栏 / Ribbon

- `VisionPro QuickBuild - [C:\Program Files\Cognex\VisionPro C++ & .NET ...] EnVisionPro10`（OCR 有误码，但可确认：**Cognex VisionPro QuickBuild**）
- 页签：`File`、`Home`、`Tool`

### 2.2 左侧面板

- **`Workspace Explorer`**（面板标题）
- 树节点：`Acquisition`、`Image Databases`

### 2.3 端子面板（★ 用户明确要求的东西）

- 面板标题：**`Inputs/Outputs`**
- 分组：**`[Inputs]`** / **`[Outputs]`**

暴露出来的端子名（这就是 VisionPro 的端子命名风格——**用"结果路径"当端子名**）：

| 工具 | 端子 |
|---|---|
| 图像输入 | `InputImage` |
| PMAlignTool1 | `Results.Count`、`Results.Item(0).GetPose().TranslationX`、`Results.Item(0).GetPose().TranslationY`、`Results.Item(0).GetPose().Rotation`、`Results.Item(0).Score` |
| LineMaxTool1 | `InputImage`、`Results.Item(0).GetLine()`、`Results.Item(0).GetLineSegment()` |
| Blob（GlobBlob） | `InputImage`、`Results.GlobBlob.Count`、`Results.GlobBlob.Item(0).CenterOfMassX`、`Results.GlobBlob.Item(0).CenterOfMassY` |

### 2.4 工具箱 / 工具块

- 分类（OCR 原样，基本准确）：
  - `Acquisition`
  - `Image Processing`
  - `Geometry - Finding & Fitting`
  - `Calibration & Fixturing`
  - `ID & Metrics`
  - `Deep Learning`
- 工具实例：`CaliperTool`、`ImageFileTool`、`PMAlignTool`、`PMRedLineTool`、`LineMaxTool1`、`JobTool` / `JobTool1`

### 2.5 底部

- **`Filmstrip`**（面板标题，实测确认）
- 条目样例：`LastRun.PMAlignTool1.InputImage`、`InputImage (C:\Program Files\Cognex\...)`
- 时间戳条目：`Time ...`

### 2.6 状态栏

- `Zoom: 175%`（实测确认）
- 另有一条疑似 `94.5ms / 123.2MB` 的性能/内存显示（OCR 误码：`94g5ms 123b2m5`，**为推断**）

---

## 三、推断（⚠️ 未经证实，仅供参考，勿当需求）

1. 布局可能是：左 = Workspace Explorer（树），中 = 图像/画布区（深色大块），右 = 属性/端子，底 = Filmstrip，最底 = 状态栏（Zoom 等）。**但 1.3 测到的深色块宽高比 4:3，更像图像显示区而非流程图画布**，所以"中间是流程图画布"这一点**存疑**。
2. 1.5 的 `y 979..1089` 密集区对应 Filmstrip；`y 1119..1129` 对应状态栏。

---

## 四、对本项目可直接落地的事实（不含推断）

- ✅ **端子要以 `[Inputs]` / `[Outputs]` 分组呈现**，端子名用可读的"结果路径"风格，而不是现在的短标签。
- ✅ **底部 Filmstrip** 是 VisionPro 的标配（显示每次运行的输入/输出图 + 时间戳）。
- ✅ **状态栏要有 Zoom 百分比**。
- ✅ **工具箱分类名**可参考：`Acquisition` / `Image Processing` / `Geometry - Finding & Fitting` / `Calibration & Fixturing` / `ID & Metrics`。
- ✅ **整体是浅色界面**。

---

## 五、历史更正说明

在拿到 OCR 结果之前，主理人曾在文档和对话中描述过参考图细节，当时**并未真正看到图**（读取工具返回"当前模型不支持图片"）。
本文件出之后，**以本文件为准**；此前关于参考图的一切描述作废。
