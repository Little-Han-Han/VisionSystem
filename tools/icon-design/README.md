# 图标设计脚本

`assets/icons/` 里应用图标的生成脚本。改图标只改这里，跑完覆盖输出。

## 产出

| 脚本 | 输出 |
|---|---|
| `generate-icons.ps1` | `assets/icons/app.ico`（9 档）、`png/app-{16,24,32,48,64,128,256}.png`、`png/splash-mark.png` |
| `make-svg.py` | `assets/icons/app-icon.svg`（磁贴版）、`app-mark.svg`（透明标记） |
| `make-ui-icons.py` | `assets/icons/ui/` 下 96 个界面图标：`src/*.svg` + `png/*-{16,24,32}.png` + `manifest.json` |

## 运行

```bat
powershell -ExecutionPolicy Bypass -File tools\icon-design\generate-icons.ps1
python tools\icon-design\make-svg.py
python tools\icon-design\make-ui-icons.py
```

三者都按自身位置回推仓库根，不需要传参。`make-ui-icons.py` 依赖 Pillow（`pip install pillow`），其余脚本零依赖。

## UI 图标约定

- 图形定义（24×24 网格、1.7px 圆头描边、线性 + 少量强调色）全部写在 `make-ui-icons.py` 末尾的 `ICONS` 表里，**改图标只改这里**，SVG 与 PNG 同源生成，不会互相漂移。
- 清单来自 `docs/设计/界面图标清单.md`；文档里重复出现的 `ui-common-save` 只生成一份，文档小节数量与去重后数量略有出入（96 个），以 `manifest.json` 为准。
- `SVG` 主色是 `currentColor`，强调色是 `var(--ui-icon-accent, #0E9BD8)`；状态类 OK/NG/WARN/ERROR 用固定语义色。
- `PNG` 是浅色主题用的深色描边版本，深色主题由代码侧反色；不要手工改 PNG。

## 注意

- **两个脚本的几何参数必须一致**（`ROut / RIn / BracketInset ...`），否则 SVG 与位图会错位。
- `.ps1` 保持**纯 ASCII**：PowerShell 5.1 按 ANSI 读脚本，中文会让引号解析失败。
- 位图走 .NET `System.Drawing`（零依赖）：2048 超采样 → 预乘 alpha 面积平均降采样；
  ico 内 ≤48 用 BMP 载荷、≥64 用 PNG 载荷。
- `< 32px` 会自动切换为简化图形（粗环、去掉角标与十字），这是刻意的光学尺寸处理，别当成 bug。
- 叶片**不留角度间隙**，缝由 7 单位深色描边形成 —— 留间隙会在透明底上漏出背景色白线。
